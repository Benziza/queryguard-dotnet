using System;
using System.Globalization;
using System.Text;

namespace QueryGuard.Reporting;

/// <summary>
/// Renders a result as plain text for a terminal or a CI log.
/// </summary>
/// <remarks>
/// Written for the reader who has this and nothing else in front of them. That means the verdict first,
/// the worst finding next, and the caveat about what repeated SQL does and does not prove included
/// rather than assumed.
/// </remarks>
public sealed class QueryGuardConsoleReporter : QueryGuardReporter
{
    /// <summary>
    /// Findings printed before the rest are summarized.
    /// </summary>
    private const int MaxReportedFindings = 10;

    /// <summary>
    /// Query groups listed before the rest are summarized.
    /// </summary>
    private const int MaxReportedGroups = 10;

    /// <inheritdoc />
    public override string FileExtension => ".txt";

    /// <inheritdoc />
    public override string Render(QueryGuardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();

        // Verdict first. A reader scanning a CI log needs to know in one line whether to keep reading.
        builder
            .Append(result.IsSuccess ? "QueryGuard passed: " : "QueryGuard FAILED: ")
            .Append(result.SessionName)
            .Append(" (policy '")
            .Append(result.PolicyName)
            .Append("')")
            .Append('\n');

        builder
            .Append(CultureInfo.InvariantCulture, $"  {result.ReadCommandCount} read queries in {result.Groups.Count} distinct queries")
            .Append(CultureInfo.InvariantCulture, $", {result.TotalDatabaseDuration.TotalMilliseconds:F1} ms database time")
            .Append('\n')
            .Append(CultureInfo.InvariantCulture, $"  {result.FailureCount} failures, {result.WarningCount} warnings, {result.IgnoredFindingCount} ignored")
            .Append('\n');

        AppendGroups(builder, result);
        AppendFindings(builder, result);

        return builder.ToString();
    }

    private static void AppendGroups(StringBuilder builder, QueryGuardResult result)
    {
        if (result.Groups.Count == 0)
        {
            return;
        }

        builder.Append('\n').Append("Queries by frequency:").Append('\n');

        var reported = Math.Min(result.Groups.Count, MaxReportedGroups);

        for (var i = 0; i < reported; i++)
        {
            var group = result.Groups[i];

            builder
                .Append(CultureInfo.InvariantCulture, $"  {group.Fingerprint.Id}  x{group.Occurrences,-4} {group.TotalDuration.TotalMilliseconds,8:F1} ms  ")
                .Append(Truncate(group.Fingerprint.NormalizedSql, 96))
                .Append('\n');
        }

        if (result.Groups.Count > reported)
        {
            // Truncation is stated. A list that quietly shows ten of forty reads as "there were ten".
            builder
                .Append(CultureInfo.InvariantCulture, $"  ... and {result.Groups.Count - reported} more distinct queries.")
                .Append('\n');
        }
    }

    private static void AppendFindings(StringBuilder builder, QueryGuardResult result)
    {
        if (result.Findings.Count == 0)
        {
            return;
        }

        builder.Append('\n').Append("Findings:").Append('\n');

        var reported = Math.Min(result.Findings.Count, MaxReportedFindings);

        for (var i = 0; i < reported; i++)
        {
            var finding = result.Findings[i];
            var label = finding.IsIgnored
                ? "IGNORED"
                : finding.Severity switch
                {
                    QueryGuardSeverity.Failure => "FAIL",
                    QueryGuardSeverity.Warning => "WARN",
                    _ => "INFO",
                };

            builder
                .Append(CultureInfo.InvariantCulture, $"  [{label}] {finding.RuleName}: {finding.Message}")
                .Append('\n');

            if (finding.IsIgnored && finding.IgnoreReason is { } reason)
            {
                builder.Append(CultureInfo.InvariantCulture, $"          reason: {reason}").Append('\n');
            }

            for (var e = 0; e < finding.Evidence.Count; e++)
            {
                builder.Append("          ").Append(finding.Evidence[e]).Append('\n');
            }

            if (finding.StackTrace is { } stackTrace)
            {
                QueryGuardOriginFormatter.Append(builder, stackTrace, "          ");
                builder.Append('\n');
            }
        }

        if (result.Findings.Count > reported)
        {
            builder
                .Append(CultureInfo.InvariantCulture, $"  ... and {result.Findings.Count - reported} more finding(s).")
                .Append('\n');
        }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), " …");
}
