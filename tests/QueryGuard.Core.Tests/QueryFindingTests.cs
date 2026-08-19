using System;
using Xunit;

namespace QueryGuard.Tests;

public class QueryFindingTests
{
    [Fact]
    public void A_finding_must_explain_itself()
    {
        // The message reaches a reader through a CI log or a test failure, usually with no
        // documentation beside it.
        Assert.Throws<ArgumentException>(() => Finding(message: " "));
        Assert.Throws<ArgumentException>(() => Finding(message: string.Empty));
    }

    [Fact]
    public void A_failure_severity_makes_the_finding_a_failure()
    {
        var finding = Finding(severity: QueryGuardSeverity.Failure);

        Assert.True(finding.IsFailure);
    }

    [Fact]
    public void A_warning_is_never_a_failure()
    {
        Assert.False(Finding(severity: QueryGuardSeverity.Warning).IsFailure);
        Assert.False(Finding(severity: QueryGuardSeverity.Information).IsFailure);
    }

    [Fact]
    public void An_ignored_failure_is_not_a_failure()
    {
        var finding = Finding(severity: QueryGuardSeverity.Failure, isIgnored: true, reason: "bounded lookup");

        Assert.False(finding.IsFailure);
        Assert.True(finding.IsIgnored);
        Assert.Equal("bounded lookup", finding.IgnoreReason);
    }

    [Fact]
    public void Expected_and_actual_values_travel_with_the_finding()
    {
        // A reader has to be able to disagree with a finding on the facts, which means the facts
        // must be in the finding rather than only in its prose.
        var finding = Finding(expected: 3, actual: 51);

        Assert.Equal(3, finding.Expected);
        Assert.Equal(51, finding.Actual);
    }

    [Fact]
    public void Evidence_and_a_missing_rule_name_default_to_empty_rather_than_null()
    {
        var finding = new QueryFinding(
            kind: QueryFindingKind.CommandFailure,
            severity: QueryGuardSeverity.Information,
            message: "Database command failed after 12 ms.",
            ruleName: null!);

        Assert.Equal(string.Empty, finding.RuleName);
        Assert.NotNull(finding.Evidence);
        Assert.Empty(finding.Evidence);
    }

    [Fact]
    public void A_stack_trace_is_absent_unless_capture_is_enabled()
    {
        // Default off, and bounded to one per fingerprint when enabled.
        // See docs/decisions/0007-stack-trace-policy.md.
        Assert.Null(Finding().StackTrace);
    }

    [Fact]
    public void An_ignored_finding_says_so_in_its_string_representation()
    {
        var ignored = Finding(severity: QueryGuardSeverity.Failure, isIgnored: true, reason: "bounded lookup");
        var reported = Finding(severity: QueryGuardSeverity.Failure);

        Assert.StartsWith("[ignored]", ignored.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("[ignored]", reported.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_session_wide_finding_has_no_fingerprint()
    {
        // A total-count budget breach concerns the whole session, not one query.
        var finding = new QueryFinding(
            kind: QueryFindingKind.TotalQueryBudget,
            severity: QueryGuardSeverity.Failure,
            message: "Request executed 27 queries; budget is 20.",
            ruleName: "max-queries",
            expected: 20,
            actual: 27);

        Assert.Null(finding.Fingerprint);
    }

    private static QueryFinding Finding(
        QueryGuardSeverity severity = QueryGuardSeverity.Warning,
        string message = "Potential N+1 pattern: fingerprint QG-FP-1A2B3C4D executed 12 times.",
        long? expected = null,
        long? actual = null,
        bool isIgnored = false,
        string? reason = null)
        => new(
            kind: QueryFindingKind.RepeatedQueryCandidate,
            severity: severity,
            message: message,
            ruleName: "repeated-query",
            fingerprint: TestData.Fingerprint(),
            expected: expected,
            actual: actual,
            isIgnored: isIgnored,
            ignoreReason: reason);
}
