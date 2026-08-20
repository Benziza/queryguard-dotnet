using System;
using System.Globalization;
using System.Text;

namespace QueryGuard.Reporting;

/// <summary>
/// Renders a baseline comparison as Markdown, for a pull request comment or a CI job summary.
/// </summary>
/// <remarks>
/// <para>
/// This is the output the rest of the baseline machinery exists to produce:
/// </para>
/// <code>
/// | Scope | Before | Now | Change |
/// | GET /api/companies | 3 | 51 | +48 |
/// </code>
/// <para>
/// A reader needs no threshold, no context, and no knowledge of what good looks like to understand that
/// row. Writing it to <c>$GITHUB_STEP_SUMMARY</c> puts it on the workflow run page, and posting it as a
/// comment puts it in the review.
/// </para>
/// <para>
/// Markdown rather than one of the reporter formats, so this does not implement
/// <see cref="IQueryGuardReporter"/>: that contract renders a single scope's findings, and a comparison
/// spans every scope in a run and has no findings at all.
/// </para>
/// </remarks>
public sealed class QueryGuardBaselineMarkdownReporter
{
    private readonly int _maxReportedScopes;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardBaselineMarkdownReporter"/> class.
    /// </summary>
    /// <param name="maxReportedScopes">
    /// How many scopes to list before summarizing the rest. A run with two hundred scopes would
    /// otherwise produce a comment nobody scrolls. Regressions sort first, so a truncated table still
    /// contains everything that got worse.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxReportedScopes"/> is not positive.</exception>
    public QueryGuardBaselineMarkdownReporter(int maxReportedScopes = 20)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxReportedScopes);

        _maxReportedScopes = maxReportedScopes;
    }

    /// <summary>
    /// Renders the comparison.
    /// </summary>
    /// <param name="comparison">The comparison to render.</param>
    /// <returns>Markdown text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="comparison"/> is <see langword="null"/>.</exception>
    public string Render(QueryGuardBaselineComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        var builder = new StringBuilder();

        builder.Append("### QueryGuard\n\n");

        if (comparison.Scopes.Count == 0)
        {
            builder.Append("No scopes were measured in this run.\n");
            return builder.ToString();
        }

        AppendVerdict(builder, comparison);
        AppendTable(builder, comparison, _maxReportedScopes);
        AppendCaveat(builder, comparison);

        return builder.ToString();
    }

    private static void AppendVerdict(StringBuilder builder, QueryGuardBaselineComparison comparison)
    {
        var regressions = comparison.Regressions.Count;
        var improvements = comparison.Improvements.Count;

        if (regressions > 0)
        {
            // The worst row, spelled out, because a table needs a sentence in front of it that says
            // what the table means.
            var worst = comparison.Regressions[0];

            builder
                .Append(CultureInfo.InvariantCulture, $"**{Plural(regressions, "scope")} now {Agree(regressions, "runs", "run")} more queries than the baseline.** ")
                .Append(CultureInfo.InvariantCulture, $"`{worst.Scope}` went from {worst.Baseline!.ReadCommands} to {worst.Current.ReadCommands}.\n\n");

            return;
        }

        if (improvements > 0)
        {
            builder
                .Append(CultureInfo.InvariantCulture, $"**{Plural(improvements, "scope")} {Agree(improvements, "runs", "run")} fewer queries than the baseline.** ")
                .Append("Nothing got worse.\n\n");

            return;
        }

        builder.Append("No change against the baseline.\n\n");
    }

    private static void AppendTable(
        StringBuilder builder,
        QueryGuardBaselineComparison comparison,
        int maxReportedScopes)
    {
        builder.Append("| Scope | Before | Now | Change |\n| --- | --: | --: | --- |\n");

        var reported = Math.Min(comparison.Scopes.Count, maxReportedScopes);

        for (var i = 0; i < reported; i++)
        {
            var scope = comparison.Scopes[i];

            var before = scope.IsNew
                ? "—"
                : scope.Baseline!.ReadCommands.ToString(CultureInfo.InvariantCulture);

            builder
                .Append("| `")
                .Append(scope.Scope.Replace("|", "\\|", StringComparison.Ordinal))
                .Append("` | ")
                .Append(before)
                .Append(" | ")
                .Append(scope.Current.ReadCommands.ToString(CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(DescribeChange(scope))
                .Append(" |\n");
        }

        if (comparison.Scopes.Count > reported)
        {
            builder
                .Append(CultureInfo.InvariantCulture, $"\n… and {comparison.Scopes.Count - reported} more scopes with no regression.\n")
                .Append('\n');
        }
        else
        {
            builder.Append('\n');
        }
    }

    private static string DescribeChange(QueryGuardScopeComparison scope)
    {
        if (scope.IsNew)
        {
            // Not a regression: a pull request that adds an endpoint must not fail for adding it.
            return "new scope";
        }

        var delta = scope.ReadCommandDelta;

        if (delta == 0 && scope.TopFingerprintDelta == 0)
        {
            return "unchanged";
        }

        var builder = new StringBuilder();

        if (delta != 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{(delta > 0 ? "+" : string.Empty)}{delta}");
        }

        // Worth its own note, because it moves when the total does not: twenty distinct lookups
        // becoming one query repeated twenty times leaves the read count identical.
        if (scope.TopFingerprintDelta > 0)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(CultureInfo.InvariantCulture, $"most-repeated query +{scope.TopFingerprintDelta}");
        }

        if (delta < 0 && scope.TopFingerprintDelta <= 0)
        {
            builder.Append(" (improved)");
        }

        return builder.ToString();
    }

    private static void AppendCaveat(StringBuilder builder, QueryGuardBaselineComparison comparison)
    {
        if (!comparison.HasRegressions)
        {
            return;
        }

        // The same caveat every other QueryGuard output carries. A count going up is a fact; whether it
        // is a defect is a judgement, and the tool does not get to make it.
        builder.Append(
            "More queries is not automatically a defect — a new feature legitimately costs queries. "
            + "If this change is intended, regenerate the baseline and commit it, so the diff records "
            + "the decision.\n");
    }

    /// <summary>
    /// Picks the verb form that agrees with a count.
    /// </summary>
    /// <remarks>
    /// The noun was already pluralised and the verb was not, so a single regression read "1 scope now
    /// run more queries". It is the first line of the pull request comment, which makes it the most
    /// read sentence the tool produces.
    /// </remarks>
    private static string Agree(int count, string singular, string plural)
        => count == 1 ? singular : plural;

    private static string Plural(int count, string noun)
        => count == 1
            ? string.Create(CultureInfo.InvariantCulture, $"1 {noun}")
            : string.Create(CultureInfo.InvariantCulture, $"{count} {noun}s");
}
