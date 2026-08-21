using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace QueryGuard.Testing;

/// <summary>
/// Assertions over a <see cref="QueryGuardResult"/>.
/// </summary>
/// <remarks>
/// The value here is not the <c>if</c> statement: anyone can write that against
/// <see cref="QueryGuardResult.IsSuccess"/>. It is the failure message. Because QueryGuard takes no test
/// framework dependency, there is no native formatter to lean on, so the message has to carry the whole
/// evidence trail itself. A failure a developer cannot act on without opening documentation is a bug in
/// this file.
/// </remarks>
public static class QueryGuardAssert
{
    /// <summary>
    /// How many findings appear in a failure message before the rest are summarized.
    /// </summary>
    /// <remarks>
    /// A pathological session can produce hundreds. Printing all of them would flood a CI log and bury
    /// the one that matters, and findings are ordered worst-first, so the first few are the useful ones.
    /// </remarks>
    private const int MaxReportedFindings = 5;

    /// <summary>
    /// How many evidence lines are printed per finding.
    /// </summary>
    private const int MaxEvidenceLinesPerFinding = 6;

    /// <summary>
    /// Where a reader goes next when they think a finding is wrong.
    /// </summary>
    private const string FalsePositiveGuide =
        "https://github.com/Benziza/queryguard-dotnet/blob/main/docs/troubleshooting/false-positives.md";

    /// <summary>
    /// Asserts that the session satisfied its policy.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="QueryGuardBudgetExceededException">The policy was not satisfied.</exception>
    /// <remarks>
    /// Warnings do not fail. A repeated-query candidate is evidence worth reading; making it fail by
    /// default would break the first build QueryGuard is installed in. Use
    /// <see cref="HasNoWarnings"/> when a test genuinely wants zero warnings.
    /// </remarks>
    public static void Passes(QueryGuardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return;
        }

        throw new QueryGuardBudgetExceededException(BuildMessage(result), result);
    }

    /// <summary>
    /// Asserts that the session produced no warnings and no failures.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="QueryGuardBudgetExceededException">A warning or failure was reported.</exception>
    /// <remarks>
    /// Stricter than <see cref="Passes"/>. Reasonable for a test that pins an endpoint's query behavior
    /// exactly; too strict as a default, since a candidate warning is often a known and documented
    /// pattern.
    /// </remarks>
    public static void HasNoWarnings(QueryGuardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.FailureCount == 0 && result.WarningCount == 0)
        {
            return;
        }

        throw new QueryGuardBudgetExceededException(BuildMessage(result), result);
    }

    /// <summary>
    /// Asserts an exact number of counted commands.
    /// </summary>
    /// <param name="expected">The expected count.</param>
    /// <param name="result">The result.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="QueryGuardBudgetExceededException">The count differed.</exception>
    /// <remarks>
    /// Exact rather than "at most", so a query count that drops unexpectedly is also caught. A refactor
    /// that removes a query is usually good news and occasionally means a feature quietly stopped
    /// loading something.
    /// </remarks>
    public static void ExecutedQueryCount(int expected, QueryGuardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var actual = CountedCommands(result);
        if (actual == expected)
        {
            return;
        }

        var message = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"QueryGuard expected exactly {expected} counted queries in {result.SessionName}, but {actual} ran.")
            .AppendLine()
            .AppendLine()
            .Append(BuildMessage(result))
            .ToString();

        throw new QueryGuardBudgetExceededException(message, result);
    }

    /// <summary>
    /// Asserts that no fingerprint occurred more than a given number of times.
    /// </summary>
    /// <param name="maxOccurrences">The maximum allowed occurrences for any one fingerprint.</param>
    /// <param name="result">The result.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxOccurrences"/> is less than one.</exception>
    /// <exception cref="QueryGuardBudgetExceededException">A fingerprint occurred too often.</exception>
    public static void NoQueryRepeatedMoreThan(int maxOccurrences, QueryGuardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (maxOccurrences < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxOccurrences),
                maxOccurrences,
                "A limit of zero would fail on the first query of any kind.");
        }

        var worst = result.TopRepeatedGroup;
        if (worst is null || worst.Occurrences <= maxOccurrences)
        {
            return;
        }

        var message = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"QueryGuard expected no query to repeat more than {maxOccurrences} times in {result.SessionName}, but {worst.Fingerprint.Id} ran {worst.Occurrences} times.")
            .AppendLine()
            .AppendLine()
            .Append(BuildMessage(result))
            .ToString();

        throw new QueryGuardBudgetExceededException(message, result);
    }

    /// <summary>
    /// Renders a result as a human-readable report.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Public because it is useful on its own: printing a passing result while tuning a budget saves
    /// guessing at the right number.
    /// </remarks>
    public static string Describe(QueryGuardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return BuildMessage(result);
    }

    private static int CountedCommands(QueryGuardResult result)
    {
        var counted = 0;
        for (var i = 0; i < result.Groups.Count; i++)
        {
            counted += result.Groups[i].Occurrences;
        }

        return counted;
    }

    private static string BuildMessage(QueryGuardResult result)
    {
        var builder = new StringBuilder();

        builder
            .Append(CultureInfo.InvariantCulture, $"QueryGuard policy '{result.PolicyName}' was not satisfied for {result.SessionName}.")
            .AppendLine()
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"  Read queries:        {result.ReadCommandCount}")
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"  Distinct queries:    {result.Groups.Count}")
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"  Database time:       {result.TotalDatabaseDuration.TotalMilliseconds:F1} ms")
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"  Failures / warnings: {result.FailureCount} / {result.WarningCount}");

        if (result.IgnoredFindingCount > 0)
        {
            builder
                .AppendLine()
                .Append(CultureInfo.InvariantCulture, $"  Ignored findings:    {result.IgnoredFindingCount}");
        }

        AppendTopRepeatedGroup(builder, result);
        AppendFindings(builder, result.Findings);

        builder
            .AppendLine()
            .AppendLine()
            .Append("If a finding is wrong, do not disable QueryGuard: record an allowlist entry with a reason, or raise")
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"the repetition threshold. See {FalsePositiveGuide}");

        return builder.ToString();
    }

    private static void AppendTopRepeatedGroup(StringBuilder builder, QueryGuardResult result)
    {
        if (result.TopRepeatedGroup is not { } top || top.Occurrences < 2)
        {
            return;
        }

        // The most repeated query, first. Of everything QueryGuard saw, this is the most likely to be
        // the actual problem, and it is what a reader should look at before anything else.
        builder
            .AppendLine()
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"  Most repeated query: {top.Fingerprint.Id} x{top.Occurrences} ({top.TotalDuration.TotalMilliseconds:F1} ms total)")
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"    {top.Fingerprint.NormalizedSql}");
    }

    private static void AppendFindings(StringBuilder builder, IReadOnlyList<QueryFinding> findings)
    {
        if (findings.Count == 0)
        {
            return;
        }

        builder.AppendLine().AppendLine().Append("  Findings:");

        var reported = Math.Min(findings.Count, MaxReportedFindings);

        for (var i = 0; i < reported; i++)
        {
            var finding = findings[i];
            var prefix = finding.IsIgnored ? "[ignored] " : string.Empty;

            builder
                .AppendLine()
                .Append(CultureInfo.InvariantCulture, $"    {prefix}{finding.Severity} {finding.RuleName}: {finding.Message}");

            if (finding.IsIgnored && finding.IgnoreReason is { } reason)
            {
                builder.AppendLine().Append(CultureInfo.InvariantCulture, $"      reason: {reason}");
            }

            var evidenceLines = Math.Min(finding.Evidence.Count, MaxEvidenceLinesPerFinding);
            for (var e = 0; e < evidenceLines; e++)
            {
                builder.AppendLine().Append(CultureInfo.InvariantCulture, $"      {finding.Evidence[e]}");
            }

            if (finding.StackTrace is { } stackTrace)
            {
                QueryGuardOriginFormatter.Append(builder, stackTrace, "      ");
            }
        }

        if (findings.Count > reported)
        {
            // Truncation is always stated. A report that quietly shows five of forty findings reads as
            // "there were five".
            builder
                .AppendLine()
                .Append(CultureInfo.InvariantCulture, $"    ... and {findings.Count - reported} more finding(s) not shown.");
        }
    }

}
