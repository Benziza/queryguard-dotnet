using System;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace QueryGuard.Tests;

public class QueryGuardAllowlistTests
{
    private const string BusyFingerprint = QueryFingerprint.IdPrefix + "busy";

    private readonly QueryGuardAnalyzer _analyzer = new();

    [Fact]
    public void An_allowlist_entry_requires_a_reason()
    {
        // An exception a reviewer cannot evaluate is not an exception, it is a blind spot.
        Assert.Throws<ArgumentException>(() => QueryGuardAllowlistEntry.ForFingerprint(BusyFingerprint, "  "));
        Assert.Throws<ArgumentException>(() => QueryGuardAllowlistEntry.ForQueryTag("bounded", string.Empty));
        Assert.Throws<ArgumentException>(() => QueryGuardPolicy.Create("p").AllowFingerprint(BusyFingerprint, null!));
    }

    [Fact]
    public void An_allowlist_entry_requires_something_to_match()
    {
        Assert.Throws<ArgumentException>(() => QueryGuardAllowlistEntry.ForFingerprint(" ", "reason"));
        Assert.Throws<ArgumentException>(() => QueryGuardAllowlistEntry.ForQueryTag(" ", "reason"));
    }

    [Fact]
    public void A_new_policy_allows_nothing()
        => Assert.Empty(QueryGuardPolicy.Create("p").Allowlist);

    [Fact]
    public void Allowlisting_returns_a_new_policy_and_leaves_the_original_untouched()
    {
        var original = QueryGuardPolicy.Create("p");

        var configured = original.AllowFingerprint(BusyFingerprint, "bounded provider lookup");

        Assert.Empty(original.Allowlist);
        Assert.Single(configured.Allowlist);
    }

    [Fact]
    public void Entries_accumulate()
    {
        var policy = QueryGuardPolicy.Create("p")
            .AllowFingerprint(BusyFingerprint, "bounded provider lookup")
            .AllowQueryTag("polling", "intentional poll, capped at ten iterations");

        Assert.Equal(2, policy.Allowlist.Count);
    }

    [Fact]
    public void Allowlisting_survives_a_policy_rename()
    {
        // The ASP.NET Core integration renames the default policy per route. Silently dropping the
        // allowlist there would resurrect every suppressed finding on every endpoint.
        var policy = QueryGuardPolicy.Create("default")
            .AllowFingerprint(BusyFingerprint, "bounded provider lookup")
            .WithName("GET /api/reports/{id}");

        Assert.Single(policy.Allowlist);
        Assert.Equal("bounded provider lookup", policy.FindAllowlistReason(BusyFingerprint, tags: null));
    }

    [Fact]
    public void Allowlisting_survives_further_budget_configuration()
    {
        var policy = QueryGuardPolicy.Create("p")
            .AllowFingerprint(BusyFingerprint, "bounded provider lookup")
            .WithMaxQueries(10)
            .WithMaxOccurrencesPerFingerprint(2);

        Assert.Single(policy.Allowlist);
    }

    [Fact]
    public void A_fingerprint_entry_matches_only_that_fingerprint()
    {
        var policy = QueryGuardPolicy.Create("p").AllowFingerprint(BusyFingerprint, "bounded provider lookup");

        Assert.Equal("bounded provider lookup", policy.FindAllowlistReason(BusyFingerprint, tags: null));
        Assert.Null(policy.FindAllowlistReason(QueryFingerprint.IdPrefix + "other", tags: null));
        Assert.Null(policy.FindAllowlistReason(null, tags: null));
    }

    [Fact]
    public void A_tag_entry_matches_the_directive_text_that_carries_it()
    {
        // A tag arrives as the whole directive, so allowlisting `bounded-lookup` has to match
        // `QueryGuard:Ignore reason=bounded-lookup`.
        var policy = QueryGuardPolicy.Create("p").AllowQueryTag("bounded-lookup", "capped at three sections");

        Assert.Equal(
            "capped at three sections",
            policy.FindAllowlistReason(null, ["QueryGuard:Ignore reason=bounded-lookup"]));
        Assert.Null(policy.FindAllowlistReason(null, ["QueryGuard:Ignore reason=something-else"]));
    }

    [Fact]
    public void An_allowlisted_candidate_is_reported_as_ignored_rather_than_removed()
    {
        // The whole point: narrow what fails without narrowing what is visible.
        var policy = QueryGuardPolicy.Create("p").AllowFingerprint(BusyFingerprint, "bounded provider lookup");

        var result = Analyze(policy, new Executed("busy", 12));
        var finding = Assert.Single(result.Findings);

        Assert.True(finding.IsIgnored);
        Assert.Equal("bounded provider lookup", finding.IgnoreReason);
        Assert.Equal(1, result.IgnoredFindingCount);
        Assert.Equal(0, result.WarningCount);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void An_allowlisted_budget_breach_is_reported_as_ignored_and_does_not_fail()
    {
        var policy = QueryGuardPolicy.Create("p")
            .WithMaxOccurrencesPerFingerprint(2)
            .AllowFingerprint(BusyFingerprint, "bounded provider lookup");

        var result = Analyze(policy, new Executed("busy", 12));

        Assert.All(result.Findings, finding => Assert.True(finding.IsIgnored));
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.FailureCount);
    }

    [Fact]
    public void Allowlisting_one_fingerprint_never_suppresses_another()
    {
        // There is deliberately no global off switch. Suppressing everything is not a supported
        // configuration, because it is indistinguishable from uninstalling the tool.
        var policy = QueryGuardPolicy.Create("p").AllowFingerprint(BusyFingerprint, "bounded provider lookup");

        var result = Analyze(policy, new Executed("busy", 12), new Executed("other", 9));

        Assert.Equal(2, result.Findings.Count);
        Assert.Equal(1, result.IgnoredFindingCount);
        Assert.Equal(1, result.WarningCount);
    }

    [Fact]
    public void A_total_query_budget_breach_cannot_be_allowlisted_by_fingerprint()
    {
        // A session-wide budget is not about one query, so a per-fingerprint exception must not
        // silence it. Otherwise allowlisting one noisy query would disable the endpoint's overall
        // guard as a side effect.
        var policy = QueryGuardPolicy.Create("p")
            .WithMaxQueries(5)
            .AllowFingerprint(BusyFingerprint, "bounded provider lookup");

        var result = Analyze(policy, new Executed("busy", 12));

        var totalBudget = Assert.Single(
            result.Findings,
            finding => finding.Kind == QueryFindingKind.TotalQueryBudget);

        Assert.False(totalBudget.IsIgnored);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void A_query_tag_directive_takes_precedence_over_a_policy_entry()
    {
        // The directive sits next to the code, so it is the more likely of the two to be current.
        var policy = QueryGuardPolicy.Create("p").AllowFingerprint(BusyFingerprint, "reason from the policy");

        var result = Analyze(policy, new Executed("busy", 12, ["QueryGuard:Ignore reason=reason-from-the-query"]));

        Assert.Equal("reason-from-the-query", Assert.Single(result.Findings).IgnoreReason);
    }

    [Fact]
    public void A_directive_without_a_reason_says_so_rather_than_inventing_one()
    {
        var result = Analyze(QueryGuardPolicy.Create("p"), new Executed("busy", 12, ["QueryGuard:Ignore"]));
        var finding = Assert.Single(result.Findings);

        Assert.True(finding.IsIgnored);
        Assert.Contains("no reason given", finding.IgnoreReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_describes_itself_for_a_report()
    {
        Assert.Equal(
            "fingerprint QG-FP-busy: bounded provider lookup",
            QueryGuardAllowlistEntry.ForFingerprint(BusyFingerprint, "bounded provider lookup").ToString());
        Assert.Equal(
            "tag polling: intentional poll",
            QueryGuardAllowlistEntry.ForQueryTag("polling", "intentional poll").ToString());
    }

    [Fact]
    public void A_null_entry_is_rejected()
        => Assert.Throws<ArgumentNullException>(() => QueryGuardPolicy.Create("p").Allow(null!));

    /// <summary>
    /// One statement executed a number of times, optionally carrying query tags.
    /// </summary>
    /// <remarks>
    /// A named type rather than a tuple: a tuple with an optional tag list needs an explicit cast on
    /// `null` to infer its type, which reads like an accident rather than a decision.
    /// </remarks>
    private sealed record Executed(string Fingerprint, int Times, IReadOnlyList<string>? Tags = null);

    private QueryGuardResult Analyze(QueryGuardPolicy policy, params Executed[] commands)
    {
        var session = new QueryGuardSession("GET /api/reports/{id}", policy);

        foreach (var (fingerprintSuffix, times, tags) in commands)
        {
            var fingerprint = new QueryFingerprint(
                QueryFingerprint.IdPrefix + fingerprintSuffix,
                string.Create(CultureInfo.InvariantCulture, $"SELECT * FROM \"{fingerprintSuffix}\" WHERE \"Id\" = ?"));

            for (var i = 0; i < times; i++)
            {
                session.Record(QueryCommandKind.Reader, fingerprint, TimeSpan.FromMilliseconds(1), tags: tags);
            }
        }

        return _analyzer.Analyze(session.Complete());
    }
}
