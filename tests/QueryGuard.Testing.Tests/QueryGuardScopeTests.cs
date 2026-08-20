using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace QueryGuard.Testing.Tests;

public class QueryGuardScopeTests
{
    [Fact]
    public void A_scope_name_is_required()
    {
        Assert.Throws<ArgumentException>(() => QueryGuardScope.Start("  "));
        Assert.Throws<ArgumentException>(() => QueryGuardScope.Start(string.Empty));
    }

    [Fact]
    public void An_open_scope_is_the_current_session_for_the_interceptor_to_find()
    {
        var accessor = new AsyncLocalQueryGuardSessionAccessor();

        using (var scope = QueryGuardScope.Start("test", accessor: accessor))
        {
            Assert.Same(scope.Session, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    [Fact]
    public void A_scope_without_a_policy_gets_one_named_after_itself()
    {
        // So a finding says which behavior it came from even when the caller did not configure a policy.
        using var scope = QueryGuardScope.Start("GET /api/companies");

        Assert.Equal("GET /api/companies", scope.Session.Policy.Name);
    }

    [Fact]
    public void Completing_returns_a_result_for_what_the_scope_recorded()
    {
        using var scope = QueryGuardScope.Start("test", QueryGuardPolicy.Create("p"));

        Record(scope, "A", 4);

        var result = scope.Complete();

        Assert.Equal(4, result.ReadCommandCount);
        Assert.Single(result.Groups);
    }

    [Fact]
    public void Completing_twice_returns_the_same_result()
    {
        // Disposal completes the scope too, so a test that completes explicitly and then disposes must
        // not get two different answers.
        using var scope = QueryGuardScope.Start("test");
        Record(scope, "A", 2);

        Assert.Same(scope.Complete(), scope.Complete());
    }

    [Fact]
    public async Task Completing_asynchronously_never_actually_waits_and_is_idempotent()
    {
        await using var scope = QueryGuardScope.Start("test");
        Record(scope, "A", 2);

        var pending = scope.CompleteAsync();

        // Analysis is in-memory, so there is nothing to await. Asserting that is the honest way to
        // document why the asynchronous overload exists at all: it reads consistently in async test
        // code, not because there is I/O behind it.
        Assert.True(pending.IsCompletedSuccessfully);

        var first = await pending;
        var second = await scope.CompleteAsync();

        Assert.Same(first, second);
    }

    [Fact]
    public async Task Completing_asynchronously_honours_cancellation()
    {
        await using var scope = QueryGuardScope.Start("test");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await scope.CompleteAsync(cancellation.Token));
    }

    [Fact]
    public void Disposal_completes_a_scope_the_caller_forgot_to_complete()
    {
        // Otherwise a test that throws before completing leaves the ambient session active, and the
        // next test on this flow records into a session nobody reads.
        var accessor = new AsyncLocalQueryGuardSessionAccessor();
        var scope = QueryGuardScope.Start("test", accessor: accessor);

        // Disposed by hand because that is the behavior under test, so try/finally covers the case
        // where an assertion fails first. Double disposal is a documented no-op.
        try
        {
            Record(scope, "A", 1);
            scope.Dispose();

            Assert.True(scope.Session.IsCompleted);
            Assert.Null(accessor.Current);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public void Disposal_after_a_failure_inside_the_scope_still_releases_the_session()
    {
        var accessor = new AsyncLocalQueryGuardSessionAccessor();

        void FailInsideScope()
        {
            using var scope = QueryGuardScope.Start("test", accessor: accessor);
            Record(scope, "A", 1);
            throw new InvalidOperationException("simulated test failure");
        }

        Assert.Throws<InvalidOperationException>(FailInsideScope);
        Assert.Null(accessor.Current);
    }

    [Fact]
    public void Disposing_twice_is_safe()
    {
        var scope = QueryGuardScope.Start("test");

        try
        {
            scope.Dispose();
            scope.Dispose();

            Assert.True(scope.Session.IsCompleted);
        }
        finally
        {
            // A third one, for good measure and to keep the analyzer's disposal path satisfied.
            scope.Dispose();
        }
    }

    [Fact]
    public async Task Nested_scopes_attribute_records_to_the_innermost_one()
    {
        var accessor = new AsyncLocalQueryGuardSessionAccessor();

        await using var outer = QueryGuardScope.Start("outer", accessor: accessor);
        Record(outer, "outer-query", 1);

        await using (var inner = QueryGuardScope.Start("inner", accessor: accessor))
        {
            Record(inner, "inner-query", 2);
            Assert.Same(inner.Session, accessor.Current);
        }

        Assert.Same(outer.Session, accessor.Current);
        Assert.Single((await outer.CompleteAsync()).Records);
    }

    [Fact]
    public async Task Concurrent_scopes_stay_isolated()
    {
        // Deliberately different counts per scope: identical counts would hide a leaked record.
        var accessor = new AsyncLocalQueryGuardSessionAccessor();

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            var expected = (index % 5) + 1;

            await using var scope = QueryGuardScope.Start(
                string.Create(CultureInfo.InvariantCulture, $"scope-{index}"),
                accessor: accessor);

            for (var i = 0; i < expected; i++)
            {
                await Task.Yield();
                Record(scope, string.Create(CultureInfo.InvariantCulture, $"query-{index}"), 1);
            }

            return (Expected: expected, Result: await scope.CompleteAsync());
        })));

        Assert.All(results, pair => Assert.Equal(pair.Expected, pair.Result.ReadCommandCount));
        Assert.Null(accessor.Current);
    }

    [Fact]
    public void The_default_accessor_is_available_for_wiring_an_interceptor_by_hand()
    {
        // The interceptor and the scope must read the same accessor, or the scope captures nothing.
        Assert.NotNull(QueryGuardScope.DefaultAccessor);

        using var scope = QueryGuardScope.Start("test");

        Assert.Same(scope.Session, QueryGuardScope.DefaultAccessor.Current);
    }

    [Fact]
    public void Capture_settings_passed_to_a_scope_are_honoured()
    {
        var redactor = new QueryGuardRedactor(new QueryGuardCaptureOptions { MaxSamplesPerFingerprint = 1 });

        using var scope = QueryGuardScope.Start("test", redactor: redactor);
        Record(scope, "A", 10);

        var group = Assert.Single(scope.Complete().Groups);

        Assert.Equal(10, group.Occurrences);
        Assert.Single(group.Samples);
    }

    internal static void Record(QueryGuardScope scope, string fingerprintSuffix, int times)
    {
        var fingerprint = new QueryFingerprint(
            QueryFingerprint.IdPrefix + fingerprintSuffix,
            string.Create(CultureInfo.InvariantCulture, $"SELECT * FROM \"{fingerprintSuffix}\" WHERE \"Id\" = ?"));

        for (var i = 0; i < times; i++)
        {
            scope.Session.Record(QueryCommandKind.Reader, fingerprint, TimeSpan.FromMilliseconds(1));
        }
    }
}
