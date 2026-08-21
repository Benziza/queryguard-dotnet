using System;
using System.Linq;
using Xunit;

namespace QueryGuard.Tests;

/// <summary>
/// Stack-trace capture: off by default, bounded to one per fingerprint, filtered when on.
/// </summary>
/// <remarks>
/// See <c>docs/decisions/0007-stack-trace-policy.md</c>. The default matters most: QueryGuard's core
/// promise is that installing it does not change how the application behaves, and a default that adds
/// hot-path allocation undermines that for a feature not everyone needs.
/// </remarks>
public class StackTraceCaptureTests
{
    [Fact]
    public void With_capture_off_the_provider_is_never_invoked()
    {
        // Not "the trace is discarded": the callback is not called at all, so nothing is walked,
        // formatted, or allocated.
        var invocations = 0;
        var session = new QueryGuardSession("test", QueryGuardPolicy.Create("p"));

        session.Record(
            QueryCommandKind.Reader,
            TestData.Fingerprint(),
            TimeSpan.Zero,
            stackTraceProvider: () =>
            {
                invocations++;
                return "   at Contoso.Api.Thing.Method()";
            });

        Assert.Equal(0, invocations);
        Assert.Null(Assert.Single(session.Complete().Records).StackTrace);
    }

    [Fact]
    public void Capture_is_off_by_default()
        => Assert.False(new QueryGuardCaptureOptions().CaptureFirstStackTrace);

    [Fact]
    public void With_capture_on_the_first_occurrence_gets_a_trace_and_the_rest_do_not()
    {
        // Bounded to one per fingerprint. There is deliberately no configuration that captures a
        // trace per command.
        var invocations = 0;
        var session = NewCapturingSession();

        for (var i = 0; i < 5; i++)
        {
            session.Record(
                QueryCommandKind.Reader,
                TestData.Fingerprint(),
                TimeSpan.Zero,
                stackTraceProvider: () =>
                {
                    invocations++;
                    return "   at Contoso.Api.Companies.CompanyService.ListDepartments()";
                });
        }

        var records = session.Complete().Records;

        Assert.Equal(1, invocations);
        Assert.NotNull(records[0].StackTrace);
        Assert.All(records.Skip(1), record => Assert.Null(record.StackTrace));
    }

    [Fact]
    public void Each_distinct_fingerprint_gets_its_own_first_trace()
    {
        var session = NewCapturingSession();

        session.Record(
            QueryCommandKind.Reader,
            TestData.FingerprintFor(1),
            TimeSpan.Zero,
            stackTraceProvider: static () => "   at Contoso.Api.First()");
        session.Record(
            QueryCommandKind.Reader,
            TestData.FingerprintFor(2),
            TimeSpan.Zero,
            stackTraceProvider: static () => "   at Contoso.Api.Second()");

        var records = session.Complete().Records;

        Assert.Contains("First", records[0].StackTrace!, StringComparison.Ordinal);
        Assert.Contains("Second", records[1].StackTrace!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_captured_trace_keeps_application_frames_and_drops_framework_ones()
    {
        var session = NewCapturingSession();

        session.Record(
            QueryCommandKind.Reader,
            TestData.Fingerprint(),
            TimeSpan.Zero,
            stackTraceProvider: static () => string.Join(
                '\n',
                "   at QueryGuard.EntityFrameworkCore.QueryGuardCommandInterceptor.ReaderExecuted()",
                "   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReader()",
                "   at System.Linq.Enumerable.ToList[T](IEnumerable`1 source)",
                "   at Contoso.Api.Companies.CompanyService.ListDepartments() in CompanyService.cs:line 42"));

        var trace = Assert.Single(session.Complete().Records).StackTrace;

        Assert.NotNull(trace);
        Assert.Contains("CompanyService.ListDepartments", trace, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryGuard.", trace, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.", trace, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trace_containing_only_framework_frames_becomes_null_rather_than_empty()
    {
        // An empty trace looks like broken capture, which is worse than no trace at all.
        var session = NewCapturingSession();

        session.Record(
            QueryCommandKind.Reader,
            TestData.Fingerprint(),
            TimeSpan.Zero,
            stackTraceProvider: static () => "   at System.Threading.Tasks.Task.Execute()");

        Assert.Null(Assert.Single(session.Complete().Records).StackTrace);
    }

    [Fact]
    public void A_captured_trace_reaches_the_finding_for_its_group()
    {
        var redactor = CapturingRedactor();
        var session = new QueryGuardSession("GET /api/companies", QueryGuardPolicy.Create("p"), redactor);

        for (var i = 0; i < 4; i++)
        {
            session.Record(
                QueryCommandKind.Reader,
                TestData.Fingerprint(),
                TimeSpan.Zero,
                stackTraceProvider: static () => "   at Contoso.Api.Companies.CompanyService.ListDepartments()");
        }

        var result = new QueryGuardAnalyzer(redactor).Analyze(session.Complete());
        var finding = Assert.Single(result.Findings);

        Assert.NotNull(finding.StackTrace);
        Assert.Contains("ListDepartments", finding.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void A_captured_trace_reaches_candidate_and_budget_findings()
    {
        var redactor = CapturingRedactor();
        var policy = QueryGuardPolicy.Create("p").WithMaxOccurrencesPerFingerprint(2);
        var session = new QueryGuardSession("GET /api/companies", policy, redactor);

        for (var i = 0; i < 4; i++)
        {
            session.Record(
                QueryCommandKind.Reader,
                TestData.Fingerprint(),
                TimeSpan.Zero,
                stackTraceProvider: static () => "   at Contoso.Api.Companies.CompanyService.ListDepartments()");
        }

        var result = new QueryGuardAnalyzer(redactor).Analyze(session.Complete());
        var candidate = Assert.Single(result.Findings, finding => finding.Kind == QueryFindingKind.RepeatedQueryCandidate);
        var budget = Assert.Single(result.Findings, finding => finding.Kind == QueryFindingKind.FingerprintOccurrenceBudget);

        Assert.Equal(candidate.StackTrace, budget.StackTrace);
        Assert.Contains("ListDepartments", budget.StackTrace!, StringComparison.Ordinal);
    }

    [Fact]
    public void With_capture_off_a_finding_has_no_stack_trace()
    {
        var session = new QueryGuardSession("GET /api/companies", QueryGuardPolicy.Create("p"));

        for (var i = 0; i < 4; i++)
        {
            session.Record(
                QueryCommandKind.Reader,
                TestData.Fingerprint(),
                TimeSpan.Zero,
                stackTraceProvider: static () => "   at Contoso.Api.Thing.Method()");
        }

        var result = new QueryGuardAnalyzer().Analyze(session.Complete());

        Assert.Null(Assert.Single(result.Findings).StackTrace);
    }

    [Fact]
    public void A_provider_returning_null_is_handled()
    {
        var session = NewCapturingSession();

        session.Record(
            QueryCommandKind.Reader,
            TestData.Fingerprint(),
            TimeSpan.Zero,
            stackTraceProvider: static () => null);

        Assert.Null(Assert.Single(session.Complete().Records).StackTrace);
    }

    [Fact]
    public void Filtered_paths_can_be_extended_for_a_shared_build_agent()
    {
        // What counts as sensitive differs between a laptop and a shared runner, so the filter list is
        // configuration rather than a constant.
        var options = new QueryGuardCaptureOptions { CaptureFirstStackTrace = true };
        options.StackTraceFrameFilters.Add("Contoso.Internal.");
        var session = new QueryGuardSession("test", QueryGuardPolicy.Create("p"), new QueryGuardRedactor(options));

        session.Record(
            QueryCommandKind.Reader,
            TestData.Fingerprint(),
            TimeSpan.Zero,
            stackTraceProvider: static () => string.Join(
                '\n',
                "   at Contoso.Internal.Secrets.Resolve()",
                "   at Contoso.Api.Public.Handler()"));

        var trace = Assert.Single(session.Complete().Records).StackTrace;

        Assert.NotNull(trace);
        Assert.DoesNotContain("Contoso.Internal", trace, StringComparison.Ordinal);
        Assert.Contains("Contoso.Api.Public", trace, StringComparison.Ordinal);
    }

    private static QueryGuardRedactor CapturingRedactor()
        => new(new QueryGuardCaptureOptions { CaptureFirstStackTrace = true });

    private static QueryGuardSession NewCapturingSession()
        => new("test", QueryGuardPolicy.Create("p"), CapturingRedactor());
}
