using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace QueryGuard.Tests;

/// <summary>
/// Repeated queries that are <em>not</em> defects, and the mechanisms that keep them controllable.
/// </summary>
/// <remarks>
/// <para>
/// QueryGuard can prove exactly one thing: the same normalized SQL ran N times in this scope. It
/// cannot prove the application-level defect. Every scenario in this file is a case where the evidence
/// is real and the conclusion "this is a bug" would be wrong.
/// </para>
/// <para>
/// These are regression fixtures, not documentation. A change to the detector that makes any of these
/// louder, or that makes the mechanism for silencing them stop working, fails here rather than
/// arriving in someone's build. Every accepted false-positive report should become one of these.
/// </para>
/// </remarks>
public class FalsePositiveFixtureTests
{
    /// <summary>
    /// A bounded lookup: a report with three sections, each fetching its own reference row.
    /// </summary>
    /// <remarks>
    /// Three occurrences hits the default threshold exactly, and the repetition is capped by the
    /// number of sections rather than by row count. Nothing here scales with data, so nothing here is
    /// an N+1, but the tool is right to mention it, because from the SQL alone it looks identical to
    /// one.
    /// </remarks>
    [Fact]
    public void A_bounded_lookup_at_the_threshold_warns_and_can_be_documented()
    {
        var reported = Analyze(QueryGuardPolicy.Create("reports"), new Executed("section-lookup", 3));

        var finding = Assert.Single(reported.Findings);
        Assert.Equal(QueryGuardSeverity.Warning, finding.Severity);
        Assert.True(reported.IsSuccess);

        // The mechanism for saying "yes, and that is fine" has to keep working.
        var documented = Analyze(
            QueryGuardPolicy.Create("reports")
                .AllowFingerprint(QueryFingerprint.IdPrefix + "section-lookup", "Three report sections; bounded by layout, not by data."),
            new Executed("section-lookup", 3));

        Assert.True(Assert.Single(documented.Findings).IsIgnored);
    }

    /// <summary>
    /// Raising the threshold is the right answer when a bounded repetition is genuinely expected.
    /// </summary>
    [Fact]
    public void Raising_the_threshold_silences_a_bounded_repetition_without_hiding_larger_ones()
    {
        var policy = QueryGuardPolicy.Create("reports").WithRepeatedQueryThreshold(6);

        Assert.Empty(Analyze(policy, new Executed("section-lookup", 5)).Findings);

        // But a genuinely unbounded pattern still surfaces.
        Assert.Single(Analyze(policy, new Executed("per-row-lookup", 51)).Findings);
    }

    /// <summary>
    /// A deliberate poll: the same query executed on a fixed schedule inside one long-lived scope.
    /// </summary>
    /// <remarks>
    /// Tagging it is better than raising the threshold, because the exception stays attached to the
    /// query that needs it instead of loosening the guard for every other query in the endpoint.
    /// </remarks>
    [Fact]
    public void An_intentional_poll_can_be_tagged_at_the_query_rather_than_loosening_the_policy()
    {
        var result = Analyze(
            QueryGuardPolicy.Create("worker"),
            new Executed("poll-status", 10, ["QueryGuard:Ignore reason=fixed-interval-poll-capped-at-ten"]));

        var finding = Assert.Single(result.Findings);

        Assert.True(finding.IsIgnored);
        Assert.Equal("fixed-interval-poll-capped-at-ten", finding.IgnoreReason);
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// A retry: the same query executed again because the first attempt failed.
    /// </summary>
    /// <remarks>
    /// The retry is the correct behavior, and the failure evidence is what makes the repetition
    /// explicable. A reader seeing both together can tell this apart from a loop.
    /// </remarks>
    [Fact]
    public void A_retried_query_reports_its_failures_alongside_the_repetition()
    {
        var session = new QueryGuardSession("worker", QueryGuardPolicy.Create("worker"));
        var fingerprint = Fingerprint("retried");

        session.Record(QueryCommandKind.Reader, fingerprint, TimeSpan.FromMilliseconds(5), isFailed: true, failureType: "Npgsql.NpgsqlException");
        session.Record(QueryCommandKind.Reader, fingerprint, TimeSpan.FromMilliseconds(5), isFailed: true, failureType: "Npgsql.NpgsqlException");
        session.Record(QueryCommandKind.Reader, fingerprint, TimeSpan.FromMilliseconds(5));

        var result = new QueryGuardAnalyzer().Analyze(session.Complete());
        var group = Assert.Single(result.Groups);

        Assert.Equal(3, group.Occurrences);
        Assert.Equal(2, group.FailureCount);
        Assert.Equal(
            2,
            result.Findings.Count(finding => finding.Kind == QueryFindingKind.CommandFailure));
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Per-tenant fan-out: one query per tenant, in a scope that deliberately handles several.
    /// </summary>
    /// <remarks>
    /// Structurally identical to an N+1 and functionally correct. There is no way to tell the two apart
    /// from SQL, which is exactly why the finding says "potential".
    /// </remarks>
    [Fact]
    public void Per_tenant_fan_out_is_reported_as_a_candidate_and_not_as_a_failure()
    {
        var result = Analyze(QueryGuardPolicy.Create("nightly-job"), new Executed("per-tenant", 40));

        var finding = Assert.Single(result.Findings);

        Assert.Contains("Potential", finding.Message, StringComparison.Ordinal);
        Assert.Equal(QueryGuardSeverity.Warning, finding.Severity);
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// A write batch: <c>SaveChanges</c> issuing one statement per entity.
    /// </summary>
    /// <remarks>
    /// This one is not merely tolerated, it is invisible: writes are never grouped for
    /// repeated-query analysis. Reporting it would put a warning on every save in the application,
    /// which is the fastest possible way to train users to ignore QueryGuard.
    /// </remarks>
    [Fact]
    public void A_write_batch_produces_no_repeated_query_finding_at_all()
    {
        var session = new QueryGuardSession("POST /api/import", QueryGuardPolicy.Create("import"));
        var fingerprint = Fingerprint("insert-row");

        for (var i = 0; i < 200; i++)
        {
            session.Record(QueryCommandKind.NonQuery, fingerprint, TimeSpan.FromMilliseconds(1));
        }

        var result = new QueryGuardAnalyzer().Analyze(session.Complete());

        Assert.Equal(200, result.Records.Count);
        Assert.Empty(result.Groups);
        Assert.Empty(result.Findings);
    }

    /// <summary>
    /// Two occurrences: the most common shape of all, and deliberately silent.
    /// </summary>
    /// <remarks>
    /// Loading the same reference row twice in one request happens constantly and is almost never
    /// worth a warning. The default threshold of three exists for this case.
    /// </remarks>
    [Fact]
    public void Two_occurrences_stay_silent_under_the_default_policy()
        => Assert.Empty(Analyze(QueryGuardPolicy.Create("default"), new Executed("reference-row", 2)).Findings);

    /// <summary>
    /// Distinct queries against the same table are not a repeated query.
    /// </summary>
    /// <remarks>
    /// If the normalizer ever merged statements that differ only in their predicate, this would start
    /// reporting a pattern that does not exist. That is the failure mode
    /// <c>docs/decisions/0005-sql-fingerprints.md</c> calls the worse of the two.
    /// </remarks>
    [Fact]
    public void Distinct_queries_against_one_table_are_not_grouped_together()
    {
        var result = Analyze(
            QueryGuardPolicy.Create("default"),
            new Executed("by-id", 1),
            new Executed("by-name", 1),
            new Executed("by-city", 1));

        Assert.Equal(3, result.Groups.Count);
        Assert.Empty(result.Findings);
    }

    private static QueryFingerprint Fingerprint(string suffix)
        => new(
            QueryFingerprint.IdPrefix + suffix,
            string.Create(CultureInfo.InvariantCulture, $"SELECT * FROM \"{suffix}\" WHERE \"Id\" = ?"));

    /// <summary>
    /// One statement executed a number of times, optionally carrying query tags.
    /// </summary>
    private sealed record Executed(string Fingerprint, int Times, IReadOnlyList<string>? Tags = null);

    private static QueryGuardResult Analyze(QueryGuardPolicy policy, params Executed[] commands)
    {
        var session = new QueryGuardSession("scope", policy);

        foreach (var (suffix, times, tags) in commands)
        {
            var fingerprint = Fingerprint(suffix);
            for (var i = 0; i < times; i++)
            {
                session.Record(QueryCommandKind.Reader, fingerprint, TimeSpan.FromMilliseconds(1), tags: tags);
            }
        }

        return new QueryGuardAnalyzer().Analyze(session.Complete());
    }
}
