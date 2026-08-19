using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace QueryGuard.Tests;

public class QueryGuardAnalyzerTests
{
    private readonly QueryGuardAnalyzer _analyzer = new();

    [Fact]
    public void A_session_is_required()
        => Assert.Throws<ArgumentNullException>(() => _analyzer.Analyze(null!));

    [Fact]
    public void An_empty_session_produces_an_empty_successful_result()
    {
        var result = Analyze(new SessionBuilder());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Groups);
        Assert.Empty(result.Findings);
        Assert.Empty(result.Records);
        Assert.Null(result.TopRepeatedGroup);
    }

    [Fact]
    public void Records_sharing_a_fingerprint_form_one_group()
    {
        var result = Analyze(new SessionBuilder().Repeat("A", 4));

        var group = Assert.Single(result.Groups);

        Assert.Equal(4, group.Occurrences);
        Assert.Equal(1, group.FirstSequence);
        Assert.Equal(4, group.LastSequence);
    }

    [Fact]
    public void A_group_aggregates_duration_and_the_sequence_range()
    {
        var result = Analyze(new SessionBuilder()
            .Repeat("A", 1, durationMs: 10)
            .Repeat("B", 1, durationMs: 1)
            .Repeat("A", 2, durationMs: 5));

        var groupA = result.Groups.Single(group => group.Fingerprint.Id.EndsWith('A'));

        Assert.Equal(3, groupA.Occurrences);
        Assert.Equal(TimeSpan.FromMilliseconds(20), groupA.TotalDuration);
        Assert.Equal(1, groupA.FirstSequence);
        Assert.Equal(4, groupA.LastSequence);
    }

    [Fact]
    public void Groups_are_ordered_with_the_most_repeated_query_first()
    {
        // A failure message leads with the top group, so this ordering is what a reader sees first.
        var result = Analyze(new SessionBuilder()
            .Repeat("quiet", 2)
            .Repeat("busy", 9)
            .Repeat("middling", 5));

        Assert.Equal(
            ["busy", "middling", "quiet"],
            result.Groups.Select(group => Suffix(group.Fingerprint.Id)));
        Assert.Equal("busy", Suffix(result.TopRepeatedGroup!.Fingerprint.Id));
    }

    [Fact]
    public void Group_ordering_is_total_so_two_identical_runs_produce_identical_reports()
    {
        // Without deterministic tie-breaking, snapshot tests are worthless and CI diffs become noise.
        var builder = new SessionBuilder()
            .Repeat("alpha", 3, durationMs: 1)
            .Repeat("bravo", 3, durationMs: 1)
            .Repeat("charlie", 3, durationMs: 1);

        var first = Analyze(builder).Groups.Select(group => group.Fingerprint.Id).ToArray();
        var second = Analyze(builder).Groups.Select(group => group.Fingerprint.Id).ToArray();

        Assert.Equal(first, second);
        Assert.Equal(first.OrderBy(id => id, StringComparer.Ordinal), first);
    }

    [Fact]
    public void Writes_are_recorded_but_not_grouped_for_repeated_query_analysis()
    {
        // Saving fifty entities is one operation that happens to issue fifty statements. Reporting it
        // as a repeated-query pattern would be noise on every SaveChanges in the application.
        var result = Analyze(new SessionBuilder().Repeat("insert", 50, kind: QueryCommandKind.NonQuery));

        Assert.Equal(50, result.Records.Count);
        Assert.Empty(result.Groups);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Below_the_threshold_no_candidate_is_reported(int occurrences)
    {
        // Two identical queries in one request is common and usually benign. The threshold errs
        // toward silence because a false positive costs more than a missed one.
        var result = Analyze(new SessionBuilder().Repeat("A", occurrences));

        Assert.Empty(result.Findings);
    }

    [Fact]
    public void At_the_threshold_a_candidate_warning_is_reported()
    {
        var result = Analyze(new SessionBuilder().Repeat("A", 3));

        var finding = Assert.Single(result.Findings);

        Assert.Equal(QueryFindingKind.RepeatedQueryCandidate, finding.Kind);
        Assert.Equal(QueryGuardSeverity.Warning, finding.Severity);
        Assert.Equal(RuleNames.RepeatedQuery, finding.RuleName);
        Assert.Equal(3, finding.Expected);
        Assert.Equal(3, finding.Actual);
    }

    [Fact]
    public void A_candidate_warning_never_fails_a_result()
    {
        // The default configuration must not break the first build QueryGuard is installed in.
        var result = Analyze(new SessionBuilder().Repeat("A", 51));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.WarningCount);
        Assert.Equal(0, result.FailureCount);
    }

    [Fact]
    public void A_candidate_says_potential_and_never_claims_detection()
    {
        // A tool that says "N+1 detected" and is wrong once teaches the user to ignore every later
        // finding. See docs/decisions/0003-detector-terminology.md.
        var finding = Assert.Single(Analyze(new SessionBuilder().Repeat("A", 12)).Findings);

        Assert.Contains("Potential", finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("detected", finding.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guaranteed", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_candidate_carries_the_evidence_that_justifies_it()
    {
        var finding = Assert.Single(Analyze(new SessionBuilder().Repeat("A", 12, durationMs: 7)).Findings);
        var evidence = string.Join('\n', finding.Evidence);

        Assert.Contains("Occurrences: 12", evidence, StringComparison.Ordinal);
        Assert.Contains("Total database time: 84.0 ms", evidence, StringComparison.Ordinal);
        Assert.Contains("First seen at command #1, last at command #12", evidence, StringComparison.Ordinal);
        Assert.Contains("SQL:", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_candidate_states_its_own_limitation_and_points_at_remediation()
    {
        // Someone reading this in a CI log usually has nothing else in front of them, so the caveat
        // travels with the finding rather than living only in the documentation.
        var finding = Assert.Single(Analyze(new SessionBuilder().Repeat("A", 5)).Findings);
        var evidence = string.Join('\n', finding.Evidence);

        Assert.Contains("strong evidence, not proof", evidence, StringComparison.Ordinal);
        Assert.Contains("eager loading", evidence, StringComparison.Ordinal);
        Assert.Contains("allowlist", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_custom_threshold_moves_the_boundary_in_both_directions()
    {
        var strict = QueryGuardPolicy.Create("strict").WithRepeatedQueryThreshold(2);
        var relaxed = QueryGuardPolicy.Create("relaxed").WithRepeatedQueryThreshold(10);

        Assert.Single(Analyze(new SessionBuilder().Repeat("A", 2), strict).Findings);
        Assert.Empty(Analyze(new SessionBuilder().Repeat("A", 9), relaxed).Findings);
        Assert.Single(Analyze(new SessionBuilder().Repeat("A", 10), relaxed).Findings);
    }

    [Fact]
    public void Several_repeated_groups_each_produce_their_own_candidate()
    {
        var result = Analyze(new SessionBuilder()
            .Repeat("A", 4)
            .Repeat("B", 6)
            .Repeat("C", 1));

        Assert.Equal(2, result.Findings.Count);
        Assert.All(result.Findings, finding => Assert.Equal(QueryFindingKind.RepeatedQueryCandidate, finding.Kind));
    }

    [Fact]
    public void A_tagged_query_is_reported_as_ignored_rather_than_removed()
    {
        // An allowlist that deletes findings becomes the place real problems go to die.
        var result = Analyze(new SessionBuilder()
            .Repeat("A", 8, tags: ["QueryGuard:Ignore reason=bounded-reference-lookup"]));

        var finding = Assert.Single(result.Findings);

        Assert.True(finding.IsIgnored);
        Assert.Equal("bounded-reference-lookup", finding.IgnoreReason);
        Assert.Equal(1, result.IgnoredFindingCount);
        Assert.Equal(0, result.WarningCount);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Ignored_findings_sort_after_reported_ones()
    {
        var result = Analyze(new SessionBuilder()
            .Repeat("ignored", 9, tags: ["QueryGuard:Ignore reason=intentional"])
            .Repeat("reported", 4));

        Assert.False(result.Findings[0].IsIgnored);
        Assert.True(result.Findings[^1].IsIgnored);
    }

    [Fact]
    public void Samples_retained_per_group_are_bounded()
    {
        var redactor = new QueryGuardRedactor(new QueryGuardCaptureOptions { MaxSamplesPerFingerprint = 2 });
        var analyzer = new QueryGuardAnalyzer(redactor);

        var group = Assert.Single(analyzer.Analyze(new SessionBuilder().Repeat("A", 500).Build()).Groups);

        Assert.Equal(500, group.Occurrences);
        Assert.Equal(2, group.Samples.Count);
        Assert.Equal(1, group.Samples[0].Sequence);
    }

    [Fact]
    public void A_group_counts_its_failed_occurrences()
    {
        var result = Analyze(new SessionBuilder()
            .Repeat("A", 2)
            .Repeat("A", 1, isFailed: true));

        var group = Assert.Single(result.Groups);

        Assert.Equal(3, group.Occurrences);
        Assert.Equal(1, group.FailureCount);
    }

    [Fact]
    public void The_result_carries_the_session_and_policy_identity()
    {
        var policy = QueryGuardPolicy.Create("companies");
        var session = new SessionBuilder("GET /api/companies").Repeat("A", 1).Build(policy);

        var result = _analyzer.Analyze(session);

        Assert.Equal("GET /api/companies", result.SessionName);
        Assert.Equal("companies", result.PolicyName);
        Assert.Equal(session.Id, result.SessionId);
    }

    private static string Suffix(string fingerprintId) => fingerprintId[QueryFingerprint.IdPrefix.Length..];

    private QueryGuardResult Analyze(SessionBuilder builder, QueryGuardPolicy? policy = null)
        => _analyzer.Analyze(builder.Build(policy));

    /// <summary>
    /// Builds a completed session from a description of what was executed, so a test can say
    /// "this fingerprint four times" instead of assembling records by hand.
    /// </summary>
    private sealed class SessionBuilder
    {
        private readonly List<Action<QueryGuardSession>> _steps = [];
        private readonly string _name;

        internal SessionBuilder(string name = "GET /api/test") => _name = name;

        internal SessionBuilder Repeat(
            string fingerprintSuffix,
            int times,
            double durationMs = 1,
            QueryCommandKind kind = QueryCommandKind.Reader,
            bool isFailed = false,
            IReadOnlyList<string>? tags = null)
        {
            _steps.Add(session =>
            {
                var fingerprint = new QueryFingerprint(
                    QueryFingerprint.IdPrefix + fingerprintSuffix,
                    string.Create(CultureInfo.InvariantCulture, $"SELECT * FROM \"{fingerprintSuffix}\" WHERE \"Id\" = ?"));

                for (var i = 0; i < times; i++)
                {
                    session.Record(
                        kind,
                        fingerprint,
                        TimeSpan.FromMilliseconds(durationMs),
                        isFailed: isFailed,
                        failureType: isFailed ? "Microsoft.Data.Sqlite.SqliteException" : null,
                        tags: tags);
                }
            });

            return this;
        }

        internal CompletedQueryGuardSession Build(QueryGuardPolicy? policy = null)
        {
            var session = new QueryGuardSession(_name, policy ?? QueryGuardPolicy.Create("test"));

            foreach (var step in _steps)
            {
                step(session);
            }

            return session.Complete();
        }
    }
}
