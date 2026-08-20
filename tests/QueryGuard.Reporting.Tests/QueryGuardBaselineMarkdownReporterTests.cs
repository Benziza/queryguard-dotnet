using System;
using System.Linq;
using Xunit;

namespace QueryGuard.Reporting.Tests;

/// <summary>
/// The pull request comment, which is the output the baseline machinery exists to produce.
/// </summary>
public class QueryGuardBaselineMarkdownReporterTests
{
    private readonly QueryGuardBaselineMarkdownReporter _reporter = new();

    [Fact]
    public void A_comparison_is_required()
        => Assert.Throws<ArgumentNullException>(() => _reporter.Render(null!));

    [Fact]
    public void A_negative_scope_limit_is_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new QueryGuardBaselineMarkdownReporter(0));

    [Fact]
    public void The_headline_names_the_worst_scope_and_both_numbers()
    {
        // A table needs a sentence in front of it saying what the table means, and "3 to 51" is the
        // whole pitch in four characters.
        var rendered = _reporter.Render(Comparison(("GET /api/companies", 3, 51)));

        Assert.Contains("GET /api/companies", rendered, StringComparison.Ordinal);
        Assert.Contains("went from 3 to 51", rendered, StringComparison.Ordinal);
        Assert.Contains("1 scope now run", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void The_table_shows_before_now_and_the_delta()
    {
        var rendered = _reporter.Render(Comparison(("GET /a", 3, 51)));

        Assert.Contains("| Scope | Before | Now | Change |", rendered, StringComparison.Ordinal);

        // Only the start of the change cell: the total went up by 48 and so did the most repeated
        // query, so the cell carries both facts. Pinning the whole row would pin the wording of the
        // second one, which a separate test already covers.
        Assert.Contains("| `GET /a` | 3 | 51 | +48", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unchanged_scope_says_so_rather_than_showing_a_zero()
    {
        var rendered = _reporter.Render(Comparison(("GET /a", 3, 3)));

        Assert.Contains("unchanged", rendered, StringComparison.Ordinal);
        Assert.Contains("No change against the baseline.", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void An_improvement_is_reported_as_one()
    {
        var rendered = _reporter.Render(Comparison(("GET /a", 51, 1)));

        Assert.Contains("run fewer queries", rendered, StringComparison.Ordinal);
        Assert.Contains("-50", rendered, StringComparison.Ordinal);
        Assert.Contains("improved", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_scope_is_shown_without_a_before_value()
    {
        var comparison = QueryGuardBaselineComparison.Compare(
            QueryGuardBaseline.Empty,
            [Result("GET /new", 5)]);

        var rendered = _reporter.Render(comparison);

        Assert.Contains("new scope", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("now run more queries", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repeated_query_growing_is_called_out_separately_from_the_total()
    {
        // The delta a total-count column cannot show: same number of reads, one query now running all
        // of them.
        var baseline = QueryGuardBaseline.Empty
            .Record(new QueryGuardBaselineEntry("GET /a", 20, 20, 1));

        var comparison = QueryGuardBaselineComparison.Compare(
            baseline,
            [ReportFixture.ResultWith("GET /a", reads: 20, groups: 1, topOccurrences: 20)]);

        var rendered = _reporter.Render(comparison);

        Assert.Contains("most-repeated query +19", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void The_caveat_travels_with_a_regression()
    {
        // Every other QueryGuard output carries it: a count going up is a fact, whether it is a defect
        // is a judgement, and the tool does not get to make it.
        var rendered = _reporter.Render(Comparison(("GET /a", 3, 51)));

        Assert.Contains("not automatically a defect", rendered, StringComparison.Ordinal);
        Assert.Contains("regenerate the baseline", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void The_caveat_is_absent_when_nothing_regressed()
        => Assert.DoesNotContain(
            "not automatically a defect",
            _reporter.Render(Comparison(("GET /a", 3, 3))),
            StringComparison.Ordinal);

    [Fact]
    public void An_empty_run_says_so_instead_of_rendering_an_empty_table()
    {
        var rendered = _reporter.Render(
            QueryGuardBaselineComparison.Compare(QueryGuardBaseline.Empty, []));

        Assert.Contains("No scopes were measured", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("| Scope |", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_run_is_truncated_and_says_how_many_it_hid()
    {
        var reporter = new QueryGuardBaselineMarkdownReporter(maxReportedScopes: 3);

        var scopes = Enumerable.Range(0, 10).Select(i => ($"GET /s{i}", 1, 1)).ToArray();
        var rendered = reporter.Render(Comparison(scopes));

        Assert.Contains("and 7 more scopes", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scope_name_containing_a_pipe_does_not_break_the_table()
    {
        var baseline = QueryGuardBaseline.Empty
            .Record(new QueryGuardBaselineEntry("GET /a|b", 1, 1, 1));

        var rendered = _reporter.Render(QueryGuardBaselineComparison.Compare(
            baseline,
            [ReportFixture.ResultWith("GET /a|b", reads: 1, groups: 1, topOccurrences: 1)]));

        Assert.Contains("GET /a\\|b", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_is_identical_across_runs_and_uses_unix_line_endings()
    {
        var comparison = Comparison(("GET /a", 3, 51));

        var rendered = _reporter.Render(comparison);

        Assert.Equal(rendered, _reporter.Render(comparison));
        Assert.DoesNotContain('\r', rendered);
    }

    private static QueryGuardBaselineComparison Comparison(params (string Scope, int Before, int Now)[] scopes)
    {
        var baseline = QueryGuardBaseline.Empty;

        foreach (var (scope, before, _) in scopes)
        {
            baseline = baseline.Record(new QueryGuardBaselineEntry(scope, before, 1, before));
        }

        return QueryGuardBaselineComparison.Compare(
            baseline,
            [.. scopes.Select(scope => Result(scope.Scope, scope.Now))]);
    }

    private static QueryGuardResult Result(string scope, int reads)
        => ReportFixture.ResultWith(scope, reads: reads, groups: 1, topOccurrences: reads);
}
