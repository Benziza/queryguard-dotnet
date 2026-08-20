using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace QueryGuard.Reporting;

/// <summary>
/// Renders a result as SARIF 2.1.0, so findings appear in GitHub code scanning.
/// </summary>
/// <remarks>
/// <para>
/// The point is not another file format. A finding now knows the file and line it came from, and SARIF is
/// built around exactly that triple — rule, message, physical location — so a repeated query can show up
/// as an annotation on the line that caused it, in the viewer CodeQL already uses. No dashboard to build
/// and nothing to install.
/// </para>
/// <para>
/// Written by hand with <see cref="Utf8JsonWriter"/> for the same reason
/// <see cref="QueryGuardJsonReporter"/> is: SARIF is a published schema, and serializing an internal
/// model into it would make the emitted shape an accident of the type layout.
/// </para>
/// <para>
/// <strong>A candidate stays a warning.</strong> Nothing here emits <c>error</c> for a repeated-query
/// finding, whatever the policy severity says about failing a build. A tool that turns "this might be an
/// N+1" into a red X gets switched off rather than tuned.
/// </para>
/// <para>
/// <strong>An ignored finding is suppressed, not omitted.</strong> Dropping allowlisted findings would
/// make the report claim something the project deliberately does not claim: that the repetition is not
/// there. SARIF has a first-class representation for "known and accepted", so it is used, with the
/// recorded reason as its justification.
/// </para>
/// <para>
/// Redaction happened before this reporter ran, and it reads nothing but the result it was handed. See
/// <c>docs/decisions/0004-parameter-privacy.md</c>.
/// </para>
/// </remarks>
public sealed class QueryGuardSarifReporter : QueryGuardReporter
{
    /// <summary>
    /// The SARIF version the emitted document declares.
    /// </summary>
    public const string SarifVersion = "2.1.0";

    private const string SchemaUri =
        "https://raw.githubusercontent.com/oasis-tcs/sarif-spectool/master/schemata/sarif-schema-2.1.0.json";

    private const string ToolName = "QueryGuard";

    private const string InformationUri = "https://benziza.github.io/queryguard-dotnet/";

    private readonly string? _repositoryRoot;
    private readonly string? _fallbackPath;
    private readonly bool _indented;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardSarifReporter"/> class.
    /// </summary>
    /// <param name="repositoryRoot">
    /// The repository root, used to turn the absolute paths a stack trace records into the
    /// repository-relative URIs GitHub needs in order to place an annotation on a diff. When it is
    /// <see langword="null"/>, or a path falls outside it, the absolute path is emitted unchanged: still
    /// valid SARIF, and the finding still appears, but without an inline annotation.
    /// </param>
    /// <param name="fallbackPath">
    /// Where to attach a finding whose origin is unknown — a repository-relative path such as the test
    /// project file. Needed because <strong>GitHub rejects a SARIF file containing any result without a
    /// location</strong>, with <c>locationFromSarifResult: expected at least one location</c>, and it
    /// rejects the whole file rather than the offending result. The SARIF schema permits omitting
    /// locations; GitHub does not. Without a fallback such a finding is left out of the document and
    /// counted in <c>runs[0].properties.findingsWithoutLocation</c>, so it is stated rather than
    /// silently dropped.
    /// </param>
    /// <param name="indented">
    /// Whether to indent. Defaults to <see langword="true"/>: the file is usually read by a person
    /// working out why an upload did not do what they expected.
    /// </param>
    public QueryGuardSarifReporter(
        string? repositoryRoot = null,
        string? fallbackPath = null,
        bool indented = true)
    {
        _repositoryRoot = NormalizeRoot(repositoryRoot);
        _fallbackPath = string.IsNullOrWhiteSpace(fallbackPath) ? null : fallbackPath.Replace('\\', '/');
        _indented = indented;
    }

    /// <inheritdoc />
    public override string FileExtension => ".sarif";

    /// <inheritdoc />
    public override string Render(QueryGuardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = _indented }))
        {
            WriteDocument(writer, result);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private void WriteDocument(Utf8JsonWriter writer, QueryGuardResult result)
    {
        // Resolved once, up front. The rule list and the results both come from it, so a rule can never
        // be declared for a finding that was left out of the document.
        var emitted = new List<(QueryFinding Finding, string Uri, int? Line)>();
        var withoutLocation = 0;

        foreach (var finding in result.Findings)
        {
            if (string.IsNullOrEmpty(finding.RuleName))
            {
                continue;
            }

            if (Location(finding) is { } location)
            {
                emitted.Add((finding, location.Uri, location.Line));
            }
            else
            {
                withoutLocation++;
            }
        }

        writer.WriteStartObject();
        writer.WriteString("$schema", SchemaUri);
        writer.WriteString("version", SarifVersion);

        writer.WriteStartArray("runs");
        writer.WriteStartObject();

        WriteTool(writer, emitted);
        WriteResults(writer, result, emitted, withoutLocation);

        writer.WriteEndObject();
        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteTool(
        Utf8JsonWriter writer,
        List<(QueryFinding Finding, string Uri, int? Line)> emitted)
    {
        writer.WriteStartObject("tool");
        writer.WriteStartObject("driver");
        writer.WriteString("name", ToolName);
        writer.WriteString("semanticVersion", ToolVersion());
        writer.WriteString("informationUri", InformationUri);

        // Only the rules this run actually produced. Declaring all seven every time would leave the
        // Security tab listing rules that never fired, which reads as coverage rather than silence.
        writer.WriteStartArray("rules");

        foreach (var ruleName in DistinctRules(emitted))
        {
            WriteRule(writer, ruleName);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static IEnumerable<string> DistinctRules(
        List<(QueryFinding Finding, string Uri, int? Line)> emitted)
        => emitted
            .Select(entry => entry.Finding.RuleName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

    private static void WriteRule(Utf8JsonWriter writer, string ruleName)
    {
        writer.WriteStartObject();
        writer.WriteString("id", ruleName);
        writer.WriteString("name", RuleTitle(ruleName));

        writer.WriteStartObject("shortDescription");
        writer.WriteString("text", RuleShortDescription(ruleName));
        writer.WriteEndObject();

        writer.WriteStartObject("fullDescription");
        writer.WriteString("text", RuleFullDescription(ruleName));
        writer.WriteEndObject();

        writer.WriteStartObject("defaultConfiguration");
        writer.WriteString("level", "warning");
        writer.WriteEndObject();

        writer.WriteStartObject("properties");
        writer.WriteStartArray("tags");
        writer.WriteStringValue("performance");
        writer.WriteStringValue("database");
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteResults(
        Utf8JsonWriter writer,
        QueryGuardResult result,
        List<(QueryFinding Finding, string Uri, int? Line)> emitted,
        int withoutLocation)
    {
        writer.WriteStartArray("results");

        foreach (var (finding, uri, line) in emitted)
        {
            writer.WriteStartObject();
            writer.WriteString("ruleId", finding.RuleName);
            writer.WriteString("level", Level(finding));

            writer.WriteStartObject("message");
            writer.WriteString("text", Message(finding, result));
            writer.WriteEndObject();

            WriteLocation(writer, uri, line);
            WriteSuppression(writer, finding);
            WriteFingerprint(writer, finding);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        // Stated rather than silent: a report that quietly shows three of five findings reads as
        // "there were three". There is nowhere in a SARIF result to say it, so it goes on the run.
        if (withoutLocation > 0)
        {
            writer.WriteStartObject("properties");
            writer.WriteNumber("findingsWithoutLocation", withoutLocation);
            writer.WriteString(
                "findingsWithoutLocationNote",
                "Omitted because GitHub rejects a SARIF file containing a result with no location. "
                + "Capture origins in the scope, or pass a fallbackPath to the reporter.");
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Maps a finding to a SARIF level.
    /// </summary>
    /// <remarks>
    /// A failed command is the only <c>error</c>: something genuinely did not work. Everything else is a
    /// <c>warning</c>, including a budget the policy would fail the build over, because the build failing
    /// is the assertion's job and does not need saying twice in a place a reader cannot tune.
    /// </remarks>
    private static string Level(QueryFinding finding)
        => string.Equals(finding.RuleName, RuleNames.CommandFailure, StringComparison.Ordinal)
            ? "error"
            : "warning";

    /// <summary>
    /// The message shown on the annotation.
    /// </summary>
    /// <remarks>
    /// The scope is appended because an annotation is read on a diff with no surrounding report:
    /// without it, "executed 50 times" does not say during what. Not appended when the message already
    /// names the scope, which the repeated-query message does — "… in GET /api/companies: … (scope: GET
    /// /api/companies)" reads like a bug, because it is one.
    /// </remarks>
    private static string Message(QueryFinding finding, QueryGuardResult result)
    {
        if (string.IsNullOrEmpty(result.SessionName)
            || finding.Message.Contains(result.SessionName, StringComparison.Ordinal))
        {
            return finding.Message;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{finding.Message} (scope: {result.SessionName})");
    }

    /// <summary>
    /// Where to attach a finding, or <see langword="null"/> when nothing can be.
    /// </summary>
    /// <remarks>
    /// A precise line when the origin was captured. Otherwise the configured fallback with no region,
    /// which is honest — it says "this run found something" without claiming to know which line. A
    /// guessed line number would annotate innocent code, which is the one outcome worse than no
    /// annotation.
    /// </remarks>
    private (string Uri, int? Line)? Location(QueryFinding finding)
    {
        if (QueryGuardOrigin.TryParse(finding.StackTrace, out var origin) && origin is not null)
        {
            return (Uri(origin.FilePath), origin.Line);
        }

        return _fallbackPath is null ? null : (_fallbackPath, null);
    }

    private static void WriteLocation(Utf8JsonWriter writer, string uri, int? startLine)
    {
        writer.WriteStartArray("locations");
        writer.WriteStartObject();
        writer.WriteStartObject("physicalLocation");

        writer.WriteStartObject("artifactLocation");
        writer.WriteString("uri", uri);
        writer.WriteEndObject();

        if (startLine is { } line)
        {
            writer.WriteStartObject("region");
            writer.WriteNumber("startLine", line);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndArray();
    }

    private static void WriteSuppression(Utf8JsonWriter writer, QueryFinding finding)
    {
        if (!finding.IsIgnored)
        {
            return;
        }

        writer.WriteStartArray("suppressions");
        writer.WriteStartObject();
        writer.WriteString("kind", "external");
        writer.WriteString("status", "accepted");

        if (!string.IsNullOrWhiteSpace(finding.IgnoreReason))
        {
            writer.WriteString("justification", finding.IgnoreReason);
        }

        writer.WriteEndObject();
        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes the stable identity GitHub uses to tell one alert from another across runs.
    /// </summary>
    /// <remarks>
    /// Without this, GitHub derives identity partly from location, so moving the offending call down a
    /// line closes one alert and opens another. The query fingerprint is exactly the right key: it is
    /// stable across edits that do not change the SQL, and it already distinguishes two different queries
    /// on the same line.
    /// </remarks>
    private static void WriteFingerprint(Utf8JsonWriter writer, QueryFinding finding)
    {
        if (finding.Fingerprint is null)
        {
            return;
        }

        writer.WriteStartObject("partialFingerprints");
        writer.WriteString("queryGuardFingerprint/v1", finding.Fingerprint.Id);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Turns a recorded source path into a SARIF artifact URI.
    /// </summary>
    /// <remarks>
    /// Relative to the repository root when possible, with forward slashes, because that is the only form
    /// GitHub can match against a diff. A path outside the root is emitted unchanged rather than
    /// mangled — a wrong relative path silently annotates the wrong file.
    /// </remarks>
    private string Uri(string filePath)
    {
        var path = filePath.Replace('\\', '/');

        if (TryStripDeterministicRoot(path, out var mapped))
        {
            return mapped;
        }

        if (_repositoryRoot is null)
        {
            return path;
        }

        if (!path.StartsWith(_repositoryRoot, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return path[_repositoryRoot.Length..].TrimStart('/');
    }

    /// <summary>
    /// Strips the synthetic root a deterministic build embeds in place of the real source directory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Found by uploading and looking at the alert. Setting <c>ContinuousIntegrationBuild</c> — which
    /// every SourceLink-using library does in CI, and which this repository does — makes the compiler
    /// replace the source root with <c>/_/</c>, so a stack trace on a CI build reports
    /// <c>/_/samples/Api/Program.cs</c> rather than the checkout path.
    /// </para>
    /// <para>
    /// That path is already repository-relative, which is the good news, but it matches no repository
    /// root, so it passed through unchanged and GitHub filed the alert under a path it could not map to
    /// the diff: the annotation silently did not appear. The upload succeeded and the tests passed.
    /// </para>
    /// <para>
    /// Numbered variants — <c>/_1/</c>, <c>/_2/</c> — appear when a build maps more than one source
    /// root, so the whole family is handled rather than the first one.
    /// </para>
    /// </remarks>
    private static bool TryStripDeterministicRoot(string path, out string mapped)
    {
        mapped = string.Empty;

        if (path.Length < 4 || path[0] != '/' || path[1] != '_')
        {
            return false;
        }

        var index = 2;
        while (index < path.Length && char.IsAsciiDigit(path[index]))
        {
            index++;
        }

        if (index >= path.Length || path[index] != '/')
        {
            return false;
        }

        mapped = path[(index + 1)..];
        return mapped.Length > 0;
    }

    private static string? NormalizeRoot(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return null;
        }

        var root = repositoryRoot.Replace('\\', '/').TrimEnd('/');

        return root.Length == 0 ? null : root + "/";
    }

    /// <remarks>
    /// The informational version, so a SARIF file names the build that produced it rather than the
    /// four-part assembly version every preview shares.
    /// </remarks>
    private static string ToolVersion()
    {
        var informational = typeof(QueryGuardSarifReporter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(QueryGuardSarifReporter).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        }

        // SARIF wants a semantic version, and SourceLink appends "+<commit>", which is a valid build
        // metadata suffix but noise in a tool banner.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);

        return plus < 0 ? informational : informational[..plus];
    }

    private static string RuleTitle(string ruleName) => ruleName switch
    {
        RuleNames.RepeatedQuery => "RepeatedQueryCandidate",
        RuleNames.MaxQueries => "MaxQueriesExceeded",
        RuleNames.MaxOccurrencesPerFingerprint => "MaxOccurrencesPerFingerprintExceeded",
        RuleNames.MaxDuplicateGroups => "MaxDuplicateGroupsExceeded",
        RuleNames.MaxTotalDuration => "MaxTotalDurationExceeded",
        RuleNames.SlowQuery => "SlowQuery",
        RuleNames.CommandFailure => "CommandFailure",
        _ => ruleName,
    };

    private static string RuleShortDescription(string ruleName) => ruleName switch
    {
        RuleNames.RepeatedQuery => "The same query ran several times in one scope.",
        RuleNames.MaxQueries => "The scope ran more queries than its budget allows.",
        RuleNames.MaxOccurrencesPerFingerprint => "One query ran more times than its budget allows.",
        RuleNames.MaxDuplicateGroups => "More distinct queries repeated than the budget allows.",
        RuleNames.MaxTotalDuration => "The scope spent longer in the database than its budget allows.",
        RuleNames.SlowQuery => "A single query took longer than the threshold.",
        RuleNames.CommandFailure => "A database command failed.",
        _ => ruleName,
    };

    private static string RuleFullDescription(string ruleName) => ruleName switch
    {
        RuleNames.RepeatedQuery =>
            "The same normalized SQL was executed several times within one scope, which is the signature "
            + "of an N+1 access pattern. It is evidence and not proof: some repetition is correct, and a "
            + "query that is legitimately repeated should be allowlisted with a written reason rather "
            + "than left to be re-reported.",
        RuleNames.MaxQueries =>
            "The scope executed more counted queries than its policy budget. A total-count budget cannot "
            + "see one query repeating more often while the total stays flat, which is why individual "
            + "fingerprints are budgeted separately.",
        RuleNames.MaxOccurrencesPerFingerprint =>
            "One SQL fingerprint was executed more times than its policy budget. This is the budget that "
            + "catches replacing several distinct lookups with one query repeated in a loop, which leaves "
            + "the total unchanged.",
        RuleNames.MaxDuplicateGroups =>
            "More distinct queries were repeated than the policy allows, which suggests repetition spread "
            + "across several access patterns rather than concentrated in one.",
        RuleNames.MaxTotalDuration =>
            "Time spent executing database commands in this scope exceeded the policy budget. Measured "
            + "from command execution only, so it excludes application time.",
        RuleNames.SlowQuery =>
            "A single command took longer than the configured threshold. QueryGuard reports the duration "
            + "it observed and does not attempt to explain it: there is no execution plan here.",
        RuleNames.CommandFailure =>
            "A database command failed. QueryGuard records the failure and rethrows, so the application "
            + "still sees the original exception.",
        _ => ruleName,
    };
}
