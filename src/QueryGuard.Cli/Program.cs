using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using QueryGuard.Reporting;

namespace QueryGuard.Cli;

/// <summary>
/// The <c>queryguard</c> command.
/// </summary>
/// <remarks>
/// <para>
/// The library can already record a baseline and compare against it. Doing that meant writing file
/// handling into a test — reading JSON, walking a directory, deciding where the repository root is. That
/// is plumbing every project would write identically, and getting it subtly wrong produces a report that
/// is silently missing rather than wrong.
/// </para>
/// <para>
/// So this tool owns the plumbing and nothing else. It does not run tests: measurement happens inside a
/// test process where the <c>DbContext</c> lives, and a tool that tried to own that would have to guess
/// the test command, the target framework, and how fixtures are wired. It reads the JSON reports the
/// test run already produced.
/// </para>
/// </remarks>
internal static class Program
{
    private const int Ok = 0;
    private const int UsageError = 1;
    private const int RegressionFound = 2;

    private const string DefaultReports = "artifacts/queryguard";
    private const string DefaultBaseline = "queryguard-baseline.json";

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            WriteUsage();
            return Ok;
        }

        if (args[0] is "--version")
        {
            Console.WriteLine(Version());
            return Ok;
        }

        if (!CommandLine.TryParse(args, out var command, out var parseError))
        {
            Console.Error.WriteLine($"queryguard: {parseError}");
            Console.Error.WriteLine();
            WriteUsage(Console.Error);
            return UsageError;
        }

        try
        {
            return command!.Command switch
            {
                "baseline record" => RecordBaseline(command),
                "verify" => Verify(command),
                _ => UnknownCommand(command.Command),
            };
        }
        catch (QueryGuardBaselineFormatException exception)
        {
            // A malformed report or baseline is the user's file to fix, so it gets a plain message
            // rather than a stack trace.
            Console.Error.WriteLine($"queryguard: {exception.Message}");
            return UsageError;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"queryguard: {exception.Message}");
            return UsageError;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"queryguard: {exception.Message}");
            return UsageError;
        }
    }

    private static int RecordBaseline(CommandLine command)
    {
        var unknown = command.FindUnknown("reports", "baseline");
        if (unknown is not null)
        {
            return Unknown(unknown);
        }

        var reportsPath = command.Option("reports", DefaultReports);
        var baselinePath = command.Option("baseline", DefaultBaseline);

        var entries = ReadReports(reportsPath, out var scanned);
        if (entries.Count == 0)
        {
            Console.Error.WriteLine($"queryguard: no QueryGuard reports found in '{reportsPath}'.");
            Console.Error.WriteLine("Have the test run write one with QueryGuardJsonReporter first.");
            return UsageError;
        }

        // Merged into whatever is already recorded, rather than replacing the file. A run that measured
        // three endpoints must not silently delete the baseline for every endpoint it did not exercise.
        var baseline = File.Exists(baselinePath)
            ? QueryGuardBaseline.FromJson(File.ReadAllText(baselinePath))
            : QueryGuardBaseline.Empty;

        var before = baseline.Count;

        foreach (var entry in entries)
        {
            baseline = baseline.Record(entry);
        }

        var directory = Path.GetDirectoryName(baselinePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(baselinePath, baseline.ToJson());

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Recorded {entries.Count} scope(s) from {scanned} report(s) into {baselinePath}."));

        if (baseline.Count > before)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{baseline.Count - before} scope(s) are new; {baseline.Count} total."));
        }

        Console.WriteLine("Commit the file. The diff is the record of what you accepted.");

        return Ok;
    }

    private static int Verify(CommandLine command)
    {
        var unknown = command.FindUnknown("reports", "baseline", "summary", "fail-on-regression");
        if (unknown is not null)
        {
            return Unknown(unknown);
        }

        var reportsPath = command.Option("reports", DefaultReports);
        var baselinePath = command.Option("baseline", DefaultBaseline);
        var summaryPath = command.OptionOrNull("summary");
        var failOnRegression = command.HasFlag("fail-on-regression");

        var entries = ReadReports(reportsPath, out var scanned);
        if (entries.Count == 0)
        {
            Console.Error.WriteLine($"queryguard: no QueryGuard reports found in '{reportsPath}'.");
            return UsageError;
        }

        if (!File.Exists(baselinePath))
        {
            // Not an error. The first run on a branch that adds the baseline has nothing to compare
            // against, and failing there would make adopting the tool the first thing it blocks.
            Console.WriteLine($"No baseline at '{baselinePath}', so there is nothing to compare against.");
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Record one with: queryguard baseline record --reports {reportsPath} --baseline {baselinePath}"));
            return Ok;
        }

        var baseline = QueryGuardBaseline.FromJson(File.ReadAllText(baselinePath));
        var comparison = QueryGuardBaselineComparison.CompareEntries(baseline, entries);

        var markdown = new QueryGuardBaselineMarkdownReporter().Render(comparison);

        if (summaryPath is not null)
        {
            var directory = Path.GetDirectoryName(summaryPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(summaryPath, markdown);
            Console.WriteLine($"Wrote {summaryPath}.");
        }

        WriteTable(comparison, scanned);

        if (!comparison.HasRegressions)
        {
            return Ok;
        }

        if (!failOnRegression)
        {
            // Reporting by default, failing on request. More queries is a fact; whether it is a defect
            // is a judgement, and the same reasoning keeps a repeated-query finding a warning.
            Console.WriteLine();
            Console.WriteLine("Pass --fail-on-regression to make this exit non-zero.");
            return Ok;
        }

        return RegressionFound;
    }

    private static void WriteTable(QueryGuardBaselineComparison comparison, int scanned)
    {
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{scanned} report(s), {comparison.Scopes.Count} scope(s) compared."));

        foreach (var scope in comparison.Scopes)
        {
            var change = scope.IsNew
                ? "new"
                : scope.ReadCommandDelta == 0 && scope.TopFingerprintDelta == 0
                    ? "unchanged"
                    : string.Create(CultureInfo.InvariantCulture, $"{scope.Baseline!.ReadCommands} -> {scope.Current.ReadCommands}");

            var marker = scope.IsRegression ? "REGRESSION" : scope.IsImprovement ? "improved  " : "          ";

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {marker} {scope.Scope}: {change}"));
        }

        if (comparison.HasRegressions)
        {
            Console.WriteLine();
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{comparison.Regressions.Count} scope(s) run more queries than the baseline."));
            Console.WriteLine("If that is intended, re-record the baseline and commit it.");
        }
    }

    /// <summary>
    /// Reads every QueryGuard report under a path.
    /// </summary>
    /// <remarks>
    /// A directory, a glob, or a single file all work, because a caller should not have to know which
    /// shape this expects. Files that are not QueryGuard reports are skipped rather than fatal — a
    /// coverage file sitting in the same directory should not stop the run.
    /// </remarks>
    private static List<QueryGuardBaselineEntry> ReadReports(string path, out int scanned)
    {
        var files = ResolveFiles(path);
        var entries = new List<QueryGuardBaselineEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        scanned = 0;

        foreach (var file in files.OrderBy(file => file, StringComparer.Ordinal))
        {
            string json;

            try
            {
                json = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            if (!QueryGuardJsonReportReader.LooksLikeReport(json))
            {
                continue;
            }

            scanned++;
            var entry = QueryGuardJsonReportReader.ReadBaselineEntry(json);

            // Later wins for a repeated scope, and the sorted order makes which one deterministic.
            if (!seen.Add(entry.Scope))
            {
                entries.RemoveAll(existing => string.Equals(existing.Scope, entry.Scope, StringComparison.Ordinal));
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static IEnumerable<string> ResolveFiles(string path)
    {
        if (File.Exists(path))
        {
            return [path];
        }

        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories);
        }

        // Treat it as a glob. Only the file name may contain a pattern, which covers
        // "artifacts/**/queryguard-*.json" style usage without implementing a matcher.
        var directory = Path.GetDirectoryName(path);
        var pattern = Path.GetFileName(path);

        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories);
    }

    /// <summary>
    /// The version to report, identifying the exact build.
    /// </summary>
    /// <remarks>
    /// The informational version rather than the assembly version, because the assembly version of every
    /// preview is <c>0.1.0.0</c> — a bug report quoting it cannot say which preview it came from. This one
    /// carries the suffix and the commit SourceLink stamped in, which is what makes a report actionable.
    /// </remarks>
    internal static string Version()
        => typeof(Program).Assembly
               .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? typeof(Program).Assembly.GetName().Version?.ToString()
           ?? "unknown";

    private static int Unknown(string name)
    {
        Console.Error.WriteLine($"queryguard: unknown option '--{name}'.");
        Console.Error.WriteLine();
        WriteUsage(Console.Error);
        return UsageError;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"queryguard: unknown command '{command}'.");
        Console.Error.WriteLine();
        WriteUsage(Console.Error);
        return UsageError;
    }

    private static void WriteUsage(TextWriter? writer = null)
    {
        writer ??= Console.Out;

        writer.WriteLine("""
            queryguard - record and verify EF Core query baselines

            Usage:
              queryguard baseline record [--reports <path>] [--baseline <file>]
              queryguard verify [--reports <path>] [--baseline <file>] [--summary <file>] [--fail-on-regression]

            Options:
              --reports <path>        Directory, glob, or file holding QueryGuard JSON reports.
                                      Default: artifacts/queryguard
              --baseline <file>       The committed baseline. Default: queryguard-baseline.json
              --summary <file>        Write the Markdown table here, for a job summary or a comment.
              --fail-on-regression    Exit 2 when a scope runs more queries than the baseline.

            Exit codes:
              0  Success, including a regression found without --fail-on-regression.
              1  Bad usage, or a file that could not be read.
              2  A regression, with --fail-on-regression.

            Your tests produce the reports; this reads them. Write one with QueryGuardJsonReporter:

              await new QueryGuardJsonReporter().WriteAsync(result, "artifacts/queryguard/companies.json");

            More: https://github.com/Benziza/queryguard-dotnet/blob/main/docs/baselines/README.md
            """);
    }
}
