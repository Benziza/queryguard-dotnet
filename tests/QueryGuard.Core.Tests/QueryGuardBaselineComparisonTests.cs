using System;
using System.Linq;
using Xunit;

namespace QueryGuard.Tests;

/// <summary>
/// Comparing a run against a baseline.
/// </summary>
/// <remarks>
/// The behaviours worth pinning are mostly about what is <em>not</em> a regression. A check that fails
/// for adding an endpoint, or for running a filtered subset of the tests, gets turned off — and a check
/// that is off protects nothing.
/// </remarks>
public class QueryGuardBaselineComparisonTests
{
    [Fact]
    public void A_scope_that_runs_more_queries_is_a_regression()
    {
        var baseline = QueryGuardBaseline.Empty
            .Record(new QueryGuardBaselineEntry("GET /api/companies", 3, 2, 2));

        var comparison = QueryGuardBaselineComparison.Compare(
            baseline,
            [TestData.ResultWith("GET /api/companies", reads: 51, groups: 2, topOccurrences: 50)]);

        var scope = Assert.Single(comparison.Scopes);

        Assert.True(scope.IsRegression);
        Assert.Equal(48, scope.ReadCommandDelta);
        Assert.Equal(48, scope.TopFingerprintDelta);
        Assert.True(comparison.HasRegressions);
    }

    [Fact]
    public void A_scope_with_the_same_counts_is_not_a_regression()
    {
        var baseline = QueryGuardBaseline.Empty
            .Record(new QueryGuardBaselineEntry("GET /a", 3, 2, 2));

        var comparison = QueryGuardBaselineComparison.Compare(
            baseline,
            [TestData.ResultWith("GET /a", reads: 3, groups: 2, topOccurrences: 2)]);

        Assert.False(comparison.HasRegressions);
        Assert.Empty(comparison.Regressions);
        Assert.Equal(0, comparison.Scopes[0].ReadCommandDelta);
    }

    [Fact]
    public void A_new_scope_is_not_a_regression()
    {
        // Otherwise the pull request that adds any endpoint fails for adding it, and the check gets
        // disabled by the second person who hits it.
        var comparison = QueryGuardBaselineComparison.Compare(
            QueryGuardBaseline.Empty,
            [TestData.ResultWith("GET /new", reads: 51, groups: 2, topOccurrences: 50)]);

        var scope = Assert.Single(comparison.Scopes);

        Assert.True(scope.IsNew);
        Assert.False(scope.IsRegression);
        Assert.Equal(0, scope.ReadCommandDelta);
        Assert.False(comparison.HasRegressions);
        Assert.Single(comparison.NewScopes);
    }

    [Fact]
    public void A_scope_missing_from_the_run_is_ignored_rather_than_reported_as_removed()
    {
        // A filtered test run would otherwise claim every endpoint it did not exercise had been
        // deleted. Being wrong that loudly is worse than saying nothing.
        var baseline = QueryGuardBaseline.Empty
            .Record(new QueryGuardBaselineEntry("GET /a", 3, 1, 3))
            .Record(new QueryGuardBaselineEntry("GET /b", 3, 1, 3));

        var comparison = QueryGuardBaselineComparison.Compare(
            baseline,
            [TestData.ResultWith("GET /a", reads: 3, groups: 1, topOccurrences: 3)]);

        Assert.Single(comparison.Scopes);
        Assert.False(comparison.HasRegressions);
    }

    [Fact]
    public void Fewer_queries_is_reported_as_an_improvement()
    {
        // A tool that only ever delivers bad news is one people stop reading.
        var baseline = QueryGuardBaseline.Empty
            .Record(new QueryGuardBaselineEntry("GET /a", 51, 2, 50));

        var comparison = QueryGuardBaselineComparison.Compare(
            baseline,
            [TestData.ResultWith("GET /a", reads: 1, groups: 1, topOccurrences: 1)]);

        var scope = Assert.Single(comparison.Scopes);

        Assert.True(scope.IsImprovement);
        Assert.False(scope.IsRegression);
        Assert.Equal(-50, scope.ReadCommandDelta);
        Assert.Single(comparison.Improvements);
    }

    [Fact]
    public void A_repeated_query_growing_is_a_regression_even_when_the_total_is_flat()
    {
        // The case a total-count budget cannot see: twenty distinct lookups become one query repeated
        // twenty times. Same number of reads, completely different behaviour.
        var baseline = QueryGuardBaseline.Empty
            .Record(new QueryGuardBaselineEntry("GET /a", 20, 20, 1));

        var comparison = QueryGuardBaselineComparison.Compare(
            baseline,
            [TestData.ResultWith("GET /a", reads: 20, groups: 1, topOccurrences: 20)]);

        var scope = Assert.Single(comparison.Scopes);

        Assert.Equal(0, scope.ReadCommandDelta);
        Assert.Equal(19, scope.TopFingerprintDelta);
        Assert.True(scope.IsRegression);
    }

    [Fact]
    public void Regressions_are_listed_first_and_ordered_deterministically()
    {
        var baseline = QueryGuardBaseline.Empty
            .Record(new QueryGuardBaselineEntry("GET /small", 1, 1, 1))
            .Record(new QueryGuardBaselineEntry("GET /big", 1, 1, 1))
            .Record(new QueryGuardBaselineEntry("GET /same", 1, 1, 1));

        var comparison = QueryGuardBaselineComparison.Compare(
            baseline,
            [
                TestData.ResultWith("GET /same", reads: 1, groups: 1, topOccurrences: 1),
                TestData.ResultWith("GET /small", reads: 3, groups: 1, topOccurrences: 3),
                TestData.ResultWith("GET /big", reads: 51, groups: 1, topOccurrences: 51),
            ]);

        Assert.Equal(
            ["GET /big", "GET /small", "GET /same"],
            comparison.Scopes.Select(scope => scope.Scope));
    }

    [Fact]
    public void Accepting_a_comparison_updates_every_measured_scope()
    {
        var baseline = QueryGuardBaseline.Empty
            .Record(new QueryGuardBaselineEntry("GET /a", 3, 1, 3))
            .Record(new QueryGuardBaselineEntry("GET /untouched", 7, 1, 7));

        var comparison = QueryGuardBaselineComparison.Compare(
            baseline,
            [TestData.ResultWith("GET /a", reads: 51, groups: 1, topOccurrences: 51)]);

        var accepted = comparison.Accept(baseline);

        Assert.Equal(51, accepted.Find("GET /a")!.ReadCommands);

        // A scope the run did not measure keeps whatever it had. Accepting one endpoint's new cost must
        // not silently erase the record for every endpoint the run skipped.
        Assert.Equal(7, accepted.Find("GET /untouched")!.ReadCommands);
    }

    [Fact]
    public void Comparing_requires_a_baseline_and_results()
    {
        Assert.Throws<ArgumentNullException>(
            () => QueryGuardBaselineComparison.Compare(null!, []));
        Assert.Throws<ArgumentNullException>(
            () => QueryGuardBaselineComparison.Compare(QueryGuardBaseline.Empty, null!));
    }
}
