using System;
using System.Linq;
using Xunit;

namespace QueryGuard.Tests;

/// <summary>
/// Recording what a scope costs, and reading it back.
/// </summary>
/// <remarks>
/// The file is committed, so it is read by a build that may not be the one that wrote it. That makes
/// round-tripping and version handling contract behaviour rather than implementation detail.
/// </remarks>
public class QueryGuardBaselineTests
{
    [Fact]
    public void An_empty_baseline_finds_nothing()
    {
        Assert.Null(QueryGuardBaseline.Empty.Find("GET /api/companies"));
        Assert.Equal(0, QueryGuardBaseline.Empty.Count);
    }

    [Fact]
    public void A_missing_scope_is_null_rather_than_an_exception()
    {
        // A scope with no baseline is the normal case on the run that introduces it, not an error.
        var baseline = QueryGuardBaseline.Empty.Record(Entry("GET /a", 3));

        Assert.Null(baseline.Find("GET /b"));
        Assert.Null(baseline.Find(null));
    }

    [Fact]
    public void Recording_returns_a_new_baseline_and_leaves_the_original_alone()
    {
        var original = QueryGuardBaseline.Empty;
        var updated = original.Record(Entry("GET /a", 3));

        Assert.Equal(0, original.Count);
        Assert.Equal(1, updated.Count);
    }

    [Fact]
    public void Recording_the_same_scope_twice_replaces_it()
    {
        var baseline = QueryGuardBaseline.Empty
            .Record(Entry("GET /a", 3))
            .Record(Entry("GET /a", 51));

        Assert.Equal(1, baseline.Count);
        Assert.Equal(51, baseline.Find("GET /a")!.ReadCommands);
    }

    [Fact]
    public void Entries_are_ordered_by_scope_so_the_committed_diff_is_stable()
    {
        // Without this the file would reorder whenever the dictionary rehashed, and every scope would
        // show as changed in a review that changed nothing.
        var baseline = QueryGuardBaseline.Empty
            .Record(Entry("GET /c", 1))
            .Record(Entry("GET /a", 1))
            .Record(Entry("GET /b", 1));

        Assert.Equal(["GET /a", "GET /b", "GET /c"], baseline.Entries.Select(entry => entry.Scope));
    }

    [Fact]
    public void A_baseline_round_trips_through_json()
    {
        var baseline = QueryGuardBaseline.Empty
            .Record(new QueryGuardBaselineEntry("GET /api/companies", 51, 2, 50))
            .Record(new QueryGuardBaselineEntry("GET /api/companies/projected", 1, 1, 1));

        var restored = QueryGuardBaseline.FromJson(baseline.ToJson());

        Assert.Equal(2, restored.Count);

        var entry = restored.Find("GET /api/companies")!;
        Assert.Equal(51, entry.ReadCommands);
        Assert.Equal(2, entry.DistinctQueries);
        Assert.Equal(50, entry.TopFingerprintOccurrences);
    }

    [Fact]
    public void The_json_is_byte_identical_across_runs()
    {
        var baseline = QueryGuardBaseline.Empty.Record(Entry("GET /a", 3));

        Assert.Equal(baseline.ToJson(), baseline.ToJson());
    }

    [Fact]
    public void The_json_ends_with_a_newline()
    {
        // It is a committed file. A missing trailing newline is a diff on the last line of every future
        // change, and some tools refuse to show it at all.
        Assert.EndsWith("\n", QueryGuardBaseline.Empty.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_json_carries_a_schema_version()
        => Assert.Contains("\"schemaVersion\": \"1.0\"", QueryGuardBaseline.Empty.ToJson(), StringComparison.Ordinal);

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":\"2.0\",\"scopes\":[]}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"scopes\":[{\"readCommands\":1}]}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"scopes\":[{\"scope\":\"GET /a\"}]}")]
    public void A_document_that_cannot_be_trusted_is_rejected_rather_than_guessed_at(string json)
    {
        // Reading a baseline wrong is worse than refusing to read it: a silently empty baseline reports
        // every scope as new, which hides every regression in the run.
        Assert.Throws<QueryGuardBaselineFormatException>(() => QueryGuardBaseline.FromJson(json));
    }

    [Fact]
    public void A_future_major_version_says_what_to_do_about_it()
    {
        var exception = Assert.Throws<QueryGuardBaselineFormatException>(
            () => QueryGuardBaseline.FromJson("{\"schemaVersion\":\"2.0\",\"scopes\":[]}"));

        Assert.Contains("2.0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Regenerate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_records_what_a_result_cost()
    {
        var result = TestData.ResultWith("GET /api/companies", reads: 51, groups: 2, topOccurrences: 50);

        var entry = QueryGuardBaselineEntry.FromResult(result);

        Assert.Equal("GET /api/companies", entry.Scope);
        Assert.Equal(51, entry.ReadCommands);
        Assert.Equal(2, entry.DistinctQueries);
        Assert.Equal(50, entry.TopFingerprintOccurrences);
    }

    [Fact]
    public void An_entry_needs_a_scope_and_non_negative_counts()
    {
        Assert.Throws<ArgumentException>(() => new QueryGuardBaselineEntry(" ", 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueryGuardBaselineEntry("s", -1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueryGuardBaselineEntry("s", 1, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueryGuardBaselineEntry("s", 1, 1, -1));
    }

    private static QueryGuardBaselineEntry Entry(string scope, int reads)
        => new(scope, reads, 1, 1);
}
