using System;
using Xunit;

namespace QueryGuard.Tests;

public class QueryGuardPolicyTests
{
    [Fact]
    public void A_new_policy_has_no_budgets_and_warns_on_the_third_repetition()
    {
        var policy = QueryGuardPolicy.Create("default");

        Assert.Null(policy.MaxQueries);
        Assert.Null(policy.MaxOccurrencesPerFingerprint);
        Assert.Null(policy.MaxDuplicateGroups);
        Assert.Null(policy.MaxTotalDuration);
        Assert.Null(policy.SlowQueryThreshold);

        // Three, not two: two identical queries in one request is common and usually benign.
        Assert.Equal(3, policy.RepeatedQueryThreshold);
        Assert.Equal(QueryGuardPolicy.DefaultRepeatedQueryThreshold, policy.RepeatedQueryThreshold);
    }

    [Fact]
    public void Reads_are_counted_by_default_and_writes_are_not()
    {
        var policy = QueryGuardPolicy.Create("default");

        Assert.True(policy.Counts(QueryCommandKind.Reader));
        Assert.True(policy.Counts(QueryCommandKind.Scalar));
        Assert.False(policy.Counts(QueryCommandKind.NonQuery));
        Assert.False(policy.Counts(QueryCommandKind.Unknown));
    }

    [Fact]
    public void A_policy_name_is_required()
    {
        Assert.Throws<ArgumentException>(() => QueryGuardPolicy.Create("  "));
        Assert.Throws<ArgumentException>(() => QueryGuardPolicy.Create(string.Empty));
    }

    [Fact]
    public void Configuring_a_budget_returns_a_new_policy_and_leaves_the_original_untouched()
    {
        // Policies are shared as singletons across concurrent requests, so a `With` method that
        // mutated in place would be a data race waiting to happen.
        var original = QueryGuardPolicy.Create("default");

        var configured = original.WithMaxQueries(20);

        Assert.Null(original.MaxQueries);
        Assert.Equal(20, configured.MaxQueries);
        Assert.NotSame(original, configured);
    }

    [Fact]
    public void Configuration_accumulates_across_calls()
    {
        var policy = QueryGuardPolicy.Create("companies")
            .WithMaxQueries(20, QueryGuardSeverity.Warning)
            .WithRepeatedQueryThreshold(4)
            .WithMaxOccurrencesPerFingerprint(5, QueryGuardSeverity.Failure)
            .WithMaxDuplicateGroups(2)
            .WithMaxTotalDuration(TimeSpan.FromMilliseconds(250))
            .WithSlowQueryThreshold(TimeSpan.FromMilliseconds(100));

        Assert.Equal("companies", policy.Name);
        Assert.Equal(20, policy.MaxQueries);
        Assert.Equal(QueryGuardSeverity.Warning, policy.MaxQueriesSeverity);
        Assert.Equal(4, policy.RepeatedQueryThreshold);
        Assert.Equal(5, policy.MaxOccurrencesPerFingerprint);
        Assert.Equal(QueryGuardSeverity.Failure, policy.MaxOccurrencesPerFingerprintSeverity);
        Assert.Equal(2, policy.MaxDuplicateGroups);
        Assert.Equal(TimeSpan.FromMilliseconds(250), policy.MaxTotalDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.SlowQueryThreshold);
    }

    [Fact]
    public void Severity_is_configured_per_rule()
    {
        // Warning on the total count while failing on a per-fingerprint breach is the most useful
        // combination, because the second rule is the one that catches an N+1 regression.
        var policy = QueryGuardPolicy.Create("mixed")
            .WithMaxQueries(20, QueryGuardSeverity.Warning)
            .WithMaxOccurrencesPerFingerprint(3, QueryGuardSeverity.Failure);

        Assert.Equal(QueryGuardSeverity.Warning, policy.MaxQueriesSeverity);
        Assert.Equal(QueryGuardSeverity.Failure, policy.MaxOccurrencesPerFingerprintSeverity);
    }

    [Fact]
    public void The_duration_budget_defaults_to_a_warning_rather_than_a_failure()
    {
        // Shared CI machines are noisy. A duration budget that fails intermittently teaches users
        // to distrust every other finding QueryGuard reports.
        var policy = QueryGuardPolicy.Create("timed").WithMaxTotalDuration(TimeSpan.FromSeconds(1));

        Assert.Equal(QueryGuardSeverity.Warning, policy.MaxTotalDurationSeverity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void A_repeated_query_threshold_below_two_is_rejected(int threshold)
    {
        // A threshold of one would report every single query as repeated, which is not a useful
        // configuration — it is a misunderstanding worth failing loudly.
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => QueryGuardPolicy.Create("bad").WithRepeatedQueryThreshold(threshold));

        Assert.Equal("threshold", exception.ParamName);
    }

    [Fact]
    public void A_negative_query_budget_is_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => QueryGuardPolicy.Create("bad").WithMaxQueries(-1));

    [Fact]
    public void A_zero_query_budget_is_allowed()
    {
        // Legitimate for an endpoint that must not touch the database at all.
        var policy = QueryGuardPolicy.Create("cache-only").WithMaxQueries(0);

        Assert.Equal(0, policy.MaxQueries);
    }

    [Fact]
    public void A_per_fingerprint_budget_of_zero_is_rejected()
    {
        // It would fail on the first query of any kind, which is never what the caller meant.
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => QueryGuardPolicy.Create("bad").WithMaxOccurrencesPerFingerprint(0));

        Assert.Equal("maxOccurrences", exception.ParamName);
    }

    [Fact]
    public void A_negative_duration_budget_is_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => QueryGuardPolicy.Create("bad").WithMaxTotalDuration(TimeSpan.FromSeconds(-1)));

    [Fact]
    public void A_policy_that_counts_no_command_kinds_is_rejected()
    {
        // It could never report anything, so accepting it would silently disable the tool.
        Assert.Throws<ArgumentException>(() => QueryGuardPolicy.Create("bad").WithCountedKinds());
        Assert.Throws<ArgumentException>(() => QueryGuardPolicy.Create("bad").WithCountedKinds(null!));
    }

    [Fact]
    public void Counted_kinds_can_be_narrowed()
    {
        var policy = QueryGuardPolicy.Create("readers-only")
            .WithCountedKinds(QueryCommandKind.Reader);

        Assert.True(policy.Counts(QueryCommandKind.Reader));
        Assert.False(policy.Counts(QueryCommandKind.Scalar));
    }

    [Fact]
    public void Mutating_the_array_passed_to_counted_kinds_does_not_change_the_policy()
    {
        var kinds = new[] { QueryCommandKind.Reader };
        var policy = QueryGuardPolicy.Create("readers-only").WithCountedKinds(kinds);

        kinds[0] = QueryCommandKind.NonQuery;

        Assert.True(policy.Counts(QueryCommandKind.Reader));
        Assert.False(policy.Counts(QueryCommandKind.NonQuery));
    }

    [Fact]
    public void Renaming_a_policy_keeps_every_configured_limit()
    {
        // The ASP.NET Core integration renames the default policy per route so that findings
        // identify the endpoint. That must not quietly reset the budgets.
        var original = QueryGuardPolicy.Create("default")
            .WithMaxQueries(15, QueryGuardSeverity.Warning)
            .WithRepeatedQueryThreshold(5)
            .WithMaxOccurrencesPerFingerprint(2)
            .WithMaxDuplicateGroups(1)
            .WithMaxTotalDuration(TimeSpan.FromMilliseconds(400))
            .WithSlowQueryThreshold(TimeSpan.FromMilliseconds(90))
            .WithCountedKinds(QueryCommandKind.Reader);

        var renamed = original.WithName("GET /api/companies");

        Assert.Equal("GET /api/companies", renamed.Name);
        Assert.Equal(original.MaxQueries, renamed.MaxQueries);
        Assert.Equal(original.MaxQueriesSeverity, renamed.MaxQueriesSeverity);
        Assert.Equal(original.RepeatedQueryThreshold, renamed.RepeatedQueryThreshold);
        Assert.Equal(original.MaxOccurrencesPerFingerprint, renamed.MaxOccurrencesPerFingerprint);
        Assert.Equal(original.MaxDuplicateGroups, renamed.MaxDuplicateGroups);
        Assert.Equal(original.MaxTotalDuration, renamed.MaxTotalDuration);
        Assert.Equal(original.SlowQueryThreshold, renamed.SlowQueryThreshold);
        Assert.False(renamed.Counts(QueryCommandKind.Scalar));
    }

    [Fact]
    public void Renaming_requires_a_name()
        => Assert.Throws<ArgumentException>(() => QueryGuardPolicy.Create("default").WithName(" "));

    [Fact]
    public void The_string_representation_names_the_policy_and_its_main_limits()
    {
        var policy = QueryGuardPolicy.Create("companies").WithMaxQueries(20);

        var text = policy.ToString();

        Assert.Contains("companies", text, StringComparison.Ordinal);
        Assert.Contains("20", text, StringComparison.Ordinal);
    }
}
