using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace QueryGuard.Tests;

public class QueryBudgetEvaluatorTests
{
    private readonly QueryGuardAnalyzer _analyzer = new();

    [Fact]
    public void Required_arguments_are_validated()
    {
        var evaluator = new QueryBudgetEvaluator();
        var session = Session(QueryGuardPolicy.Create("p"), ("A", 1, 1d));

        Assert.Throws<ArgumentNullException>(() => evaluator.Evaluate(null!, Array.Empty<QueryFingerprintGroup>()));
        Assert.Throws<ArgumentNullException>(() => evaluator.Evaluate(session, null!));
    }

    [Fact]
    public void A_policy_with_no_budgets_produces_no_budget_findings()
    {
        // Installing QueryGuard has to be safe. It reports what it sees before it starts failing
        // anything, so every budget is opt-in.
        var result = _analyzer.Analyze(Session(QueryGuardPolicy.Create("bare"), ("A", 40, 1d)));

        Assert.True(result.IsSuccess);
        Assert.All(result.Findings, finding => Assert.Equal(QueryFindingKind.RepeatedQueryCandidate, finding.Kind));
    }

    // ---------------------------------------------------------------------
    // Total query budget
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public void Total_queries_at_or_below_the_budget_pass(int executed)
    {
        // Exactly at the budget passes. A budget is a maximum, and an off-by-one here would make
        // every user's carefully chosen number quietly one lower than they wrote.
        var policy = QueryGuardPolicy.Create("p").WithMaxQueries(10);

        var result = _analyzer.Analyze(Session(policy, ("A", executed, 1d)));

        Assert.DoesNotContain(result.Findings, finding => finding.Kind == QueryFindingKind.TotalQueryBudget);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void One_query_over_the_budget_fails_with_expected_and_actual()
    {
        var policy = QueryGuardPolicy.Create("p").WithMaxQueries(10);

        var result = _analyzer.Analyze(Session(policy, ("A", 11, 1d)));
        var finding = Single(result, QueryFindingKind.TotalQueryBudget);

        Assert.Equal(QueryGuardSeverity.Failure, finding.Severity);
        Assert.Equal(RuleNames.MaxQueries, finding.RuleName);
        Assert.Equal(10, finding.Expected);
        Assert.Equal(11, finding.Actual);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void The_total_query_budget_severity_is_configurable()
    {
        var policy = QueryGuardPolicy.Create("p").WithMaxQueries(1, QueryGuardSeverity.Warning);

        var result = _analyzer.Analyze(Session(policy, ("A", 5, 1d)));

        Assert.Equal(QueryGuardSeverity.Warning, Single(result, QueryFindingKind.TotalQueryBudget).Severity);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Writes_do_not_count_toward_the_total_query_budget()
    {
        var policy = QueryGuardPolicy.Create("p").WithMaxQueries(1);
        var session = new QueryGuardSession("test", policy);

        Record(session, "read", 1);
        Record(session, "write", 20, QueryCommandKind.NonQuery);

        var result = _analyzer.Analyze(session.Complete());

        Assert.DoesNotContain(result.Findings, finding => finding.Kind == QueryFindingKind.TotalQueryBudget);
    }

    [Fact]
    public void A_zero_query_budget_fails_on_the_first_query()
    {
        // Legitimate for an endpoint that must not touch the database at all.
        var policy = QueryGuardPolicy.Create("cache-only").WithMaxQueries(0);

        Assert.Empty(_analyzer.Analyze(Session(policy)).Findings);
        Assert.False(_analyzer.Analyze(Session(policy, ("A", 1, 1d))).IsSuccess);
    }

    // ---------------------------------------------------------------------
    // Per-fingerprint occurrence budget
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void A_fingerprint_at_or_below_its_budget_passes(int occurrences)
    {
        var policy = QueryGuardPolicy.Create("p").WithMaxOccurrencesPerFingerprint(5);

        var result = _analyzer.Analyze(Session(policy, ("A", occurrences, 1d)));

        Assert.DoesNotContain(
            result.Findings,
            finding => finding.Kind == QueryFindingKind.FingerprintOccurrenceBudget);
    }

    [Fact]
    public void A_fingerprint_over_its_budget_is_identified_by_name()
    {
        // This is the rule that actually catches an N+1 regression: a total-count budget can stay
        // satisfied while one query quietly repeats.
        var policy = QueryGuardPolicy.Create("p").WithMaxOccurrencesPerFingerprint(5);

        var result = _analyzer.Analyze(Session(policy, ("busy", 51, 1d), ("quiet", 1, 1d)));
        var finding = Single(result, QueryFindingKind.FingerprintOccurrenceBudget);

        Assert.Equal(QueryFingerprint.IdPrefix + "busy", finding.Fingerprint!.Id);
        Assert.Equal(5, finding.Expected);
        Assert.Equal(51, finding.Actual);
        Assert.Contains("SQL:", string.Join('\n', finding.Evidence), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_offending_fingerprint_produces_its_own_finding()
    {
        var policy = QueryGuardPolicy.Create("p").WithMaxOccurrencesPerFingerprint(2);

        var result = _analyzer.Analyze(Session(policy, ("A", 5, 1d), ("B", 4, 1d), ("C", 2, 1d)));

        Assert.Equal(
            2,
            result.Findings.Count(finding => finding.Kind == QueryFindingKind.FingerprintOccurrenceBudget));
    }

    // ---------------------------------------------------------------------
    // Duplicate group budget
    // ---------------------------------------------------------------------

    [Fact]
    public void One_repeated_group_within_the_duplicate_budget_passes()
    {
        var policy = QueryGuardPolicy.Create("p").WithMaxDuplicateGroups(1);

        var result = _analyzer.Analyze(Session(policy, ("A", 5, 1d), ("B", 1, 1d)));

        Assert.DoesNotContain(result.Findings, finding => finding.Kind == QueryFindingKind.DuplicateGroupBudget);
    }

    [Fact]
    public void Too_many_repeated_groups_produces_one_finding_listing_all_of_them()
    {
        // One repeated group is usually a single bug; several at once is a structural problem, and
        // the finding says which of the two this is.
        var policy = QueryGuardPolicy.Create("p").WithMaxDuplicateGroups(1);

        var result = _analyzer.Analyze(Session(policy, ("A", 5, 1d), ("B", 4, 1d), ("C", 3, 1d)));
        var finding = Single(result, QueryFindingKind.DuplicateGroupBudget);

        Assert.Equal(1, finding.Expected);
        Assert.Equal(3, finding.Actual);

        var evidence = string.Join('\n', finding.Evidence);
        Assert.Contains("QG-FP-A: 5 occurrences", evidence, StringComparison.Ordinal);
        Assert.Contains("QG-FP-B: 4 occurrences", evidence, StringComparison.Ordinal);
        Assert.Contains("QG-FP-C: 3 occurrences", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void The_duplicate_budget_counts_groups_at_the_repetition_threshold()
    {
        // Groups below the threshold are not "repeated" for this rule's purposes, so raising the
        // threshold reduces the count the budget sees.
        var policy = QueryGuardPolicy.Create("p")
            .WithMaxDuplicateGroups(0)
            .WithRepeatedQueryThreshold(5);

        var withinThreshold = _analyzer.Analyze(Session(policy, ("A", 4, 1d), ("B", 4, 1d)));
        var atThreshold = _analyzer.Analyze(Session(policy, ("A", 5, 1d)));

        Assert.DoesNotContain(
            withinThreshold.Findings,
            finding => finding.Kind == QueryFindingKind.DuplicateGroupBudget);
        Assert.Contains(atThreshold.Findings, finding => finding.Kind == QueryFindingKind.DuplicateGroupBudget);
    }

    // ---------------------------------------------------------------------
    // Duration budgets
    // ---------------------------------------------------------------------

    [Fact]
    public void The_duration_budget_does_not_fire_when_it_is_not_configured()
    {
        // Off by default. A duration budget that fires intermittently on a shared runner teaches
        // users to distrust every other finding QueryGuard reports.
        var result = _analyzer.Analyze(Session(QueryGuardPolicy.Create("p"), ("A", 10, 500d)));

        Assert.DoesNotContain(result.Findings, finding => finding.Kind == QueryFindingKind.TotalDurationBudget);
    }

    [Fact]
    public void An_exceeded_duration_budget_warns_and_says_why_timing_is_unreliable()
    {
        var policy = QueryGuardPolicy.Create("p").WithMaxTotalDuration(TimeSpan.FromMilliseconds(100));

        var result = _analyzer.Analyze(Session(policy, ("A", 4, 50d)));
        var finding = Single(result, QueryFindingKind.TotalDurationBudget);

        Assert.Equal(QueryGuardSeverity.Warning, finding.Severity);
        Assert.True(result.IsSuccess);
        Assert.Contains(
            "varies with machine load",
            string.Join('\n', finding.Evidence),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Duration_exactly_at_the_budget_passes()
    {
        var policy = QueryGuardPolicy.Create("p").WithMaxTotalDuration(TimeSpan.FromMilliseconds(100));

        var result = _analyzer.Analyze(Session(policy, ("A", 4, 25d)));

        Assert.DoesNotContain(result.Findings, finding => finding.Kind == QueryFindingKind.TotalDurationBudget);
    }

    [Fact]
    public void A_slow_query_is_reported_once_per_fingerprint_rather_than_once_per_command()
    {
        // One slow query executed fifty times is one problem, not fifty findings.
        var policy = QueryGuardPolicy.Create("p").WithSlowQueryThreshold(TimeSpan.FromMilliseconds(50));

        var result = _analyzer.Analyze(Session(policy, ("slow", 20, 120d), ("fast", 5, 1d)));

        var finding = Single(result, QueryFindingKind.SlowQuery);
        Assert.Equal(QueryFingerprint.IdPrefix + "slow", finding.Fingerprint!.Id);
    }

    [Fact]
    public void Slow_query_detection_is_off_unless_a_threshold_is_set()
    {
        var result = _analyzer.Analyze(Session(QueryGuardPolicy.Create("p"), ("A", 1, 5_000d)));

        Assert.DoesNotContain(result.Findings, finding => finding.Kind == QueryFindingKind.SlowQuery);
    }

    // ---------------------------------------------------------------------
    // Failures and allowlists
    // ---------------------------------------------------------------------

    [Fact]
    public void A_failed_command_is_reported_as_information_beside_the_original_exception()
    {
        // The application already threw, and its exception is the real report. QueryGuard adds
        // context next to it and never competes with it.
        var session = new QueryGuardSession("test", QueryGuardPolicy.Create("p"));
        Record(session, "A", 1, isFailed: true);

        var result = _analyzer.Analyze(session.Complete());
        var finding = Single(result, QueryFindingKind.CommandFailure);

        Assert.Equal(QueryGuardSeverity.Information, finding.Severity);
        Assert.True(result.IsSuccess);
        Assert.Contains(
            "original exception is unchanged",
            string.Join('\n', finding.Evidence),
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_allowlisted_fingerprint_still_reports_its_budget_breach_as_ignored()
    {
        var policy = QueryGuardPolicy.Create("p").WithMaxOccurrencesPerFingerprint(2);
        var session = new QueryGuardSession("test", policy);
        Record(session, "A", 9, tags: ["QueryGuard:Ignore reason=bounded-provider-lookup"]);

        var result = _analyzer.Analyze(session.Complete());
        var finding = Single(result, QueryFindingKind.FingerprintOccurrenceBudget);

        Assert.True(finding.IsIgnored);
        Assert.Equal("bounded-provider-lookup", finding.IgnoreReason);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.FailureCount);
    }

    // ---------------------------------------------------------------------
    // Composition and ordering
    // ---------------------------------------------------------------------

    [Fact]
    public void Failures_sort_before_warnings_across_both_finding_sources()
    {
        // Candidates come from the analyzer and budgets from the evaluator. Ordering is applied once
        // over the combined set, so a reader scanning CI output sees the verdict first.
        var policy = QueryGuardPolicy.Create("p")
            .WithMaxQueries(1)
            .WithMaxTotalDuration(TimeSpan.FromMilliseconds(1), QueryGuardSeverity.Warning);

        var result = _analyzer.Analyze(Session(policy, ("A", 6, 10d)));

        Assert.True(result.Findings.Count >= 3);
        Assert.Equal(QueryGuardSeverity.Failure, result.Findings[0].Severity);
        Assert.Equal(QueryGuardSeverity.Warning, result.Findings[^1].Severity);
    }

    [Fact]
    public void A_custom_evaluator_replaces_the_built_in_rules()
    {
        var analyzer = new QueryGuardAnalyzer(budgetEvaluator: new NothingIsEverAcceptable());

        var result = analyzer.Analyze(Session(QueryGuardPolicy.Create("p"), ("A", 1, 1d)));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Findings, finding => finding.RuleName == "house-rules");
    }

    private static QueryFinding Single(QueryGuardResult result, QueryFindingKind kind)
        => Assert.Single(result.Findings, finding => finding.Kind == kind);

    private static CompletedQueryGuardSession Session(
        QueryGuardPolicy policy,
        params (string Fingerprint, int Times, double DurationMs)[] commands)
    {
        var session = new QueryGuardSession("GET /api/test", policy);

        foreach (var (fingerprint, times, durationMs) in commands)
        {
            Record(session, fingerprint, times, durationMs: durationMs);
        }

        return session.Complete();
    }

    private static void Record(
        QueryGuardSession session,
        string fingerprintSuffix,
        int times,
        QueryCommandKind kind = QueryCommandKind.Reader,
        double durationMs = 1,
        bool isFailed = false,
        IReadOnlyList<string>? tags = null)
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
    }

    private sealed class NothingIsEverAcceptable : IQueryBudgetEvaluator
    {
        public IReadOnlyList<QueryFinding> Evaluate(
            CompletedQueryGuardSession session,
            IReadOnlyList<QueryFingerprintGroup> groups)
            =>
            [
                new QueryFinding(
                    QueryFindingKind.TotalQueryBudget,
                    QueryGuardSeverity.Failure,
                    "This team does not allow database access.",
                    "house-rules"),
            ];
    }
}
