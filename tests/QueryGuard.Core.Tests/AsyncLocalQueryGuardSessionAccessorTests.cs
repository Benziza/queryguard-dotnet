using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace QueryGuard.Tests;

public class AsyncLocalQueryGuardSessionAccessorTests
{
    [Fact]
    public void No_session_is_current_before_anything_is_activated()
    {
        var accessor = new AsyncLocalQueryGuardSessionAccessor();

        // Null means capture nothing. QueryGuard stays silent rather than guessing which scope a
        // command belongs to.
        Assert.Null(accessor.Current);
    }

    [Fact]
    public void Activating_a_session_makes_it_current_and_disposal_clears_it()
    {
        var accessor = new AsyncLocalQueryGuardSessionAccessor();
        var session = TestData.Session();

        using (var activation = accessor.Activate(session))
        {
            Assert.Same(session, accessor.Current);
            Assert.Same(session, activation.Session);
        }

        Assert.Null(accessor.Current);
    }

    [Fact]
    public void A_session_is_required()
    {
        var accessor = new AsyncLocalQueryGuardSessionAccessor();

        Assert.Throws<ArgumentNullException>(() => accessor.Activate(null!));
    }

    [Fact]
    public void A_nested_session_shadows_its_parent_and_disposal_restores_it()
    {
        var accessor = new AsyncLocalQueryGuardSessionAccessor();
        var parent = TestData.Session("parent");
        var child = TestData.Session("child");

        using (accessor.Activate(parent))
        {
            Assert.Same(parent, accessor.Current);

            using (accessor.Activate(child))
            {
                Assert.Same(child, accessor.Current);
            }

            Assert.Same(parent, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    [Fact]
    public void The_parent_session_is_restored_when_a_nested_scope_throws()
    {
        // The exception path is the part that actually breaks. A scope that only unwinds cleanly on
        // success corrupts every measurement taken after the first failing test.
        var accessor = new AsyncLocalQueryGuardSessionAccessor();
        var parent = TestData.Session("parent");
        var child = TestData.Session("child");

        using (accessor.Activate(parent))
        {
            // A local function rather than an inline lambda: a block that ends in `throw` is
            // convertible to several Assert.Throws overloads, and the one it binds to is the
            // obsolete Func<Task> variant.
            void FailInsideNestedScope()
            {
                using (accessor.Activate(child))
                {
                    Assert.Same(child, accessor.Current);
                    throw new InvalidOperationException("simulated endpoint failure");
                }
            }

            Assert.Throws<InvalidOperationException>(FailInsideNestedScope);

            Assert.Same(parent, accessor.Current);
        }
    }

    [Fact]
    public void Deeply_nested_sessions_unwind_in_order()
    {
        var accessor = new AsyncLocalQueryGuardSessionAccessor();
        var sessions = new[]
        {
            TestData.Session("level-0"),
            TestData.Session("level-1"),
            TestData.Session("level-2"),
            TestData.Session("level-3"),
        };

        // Disposal order is what this test is about, so the activations are held individually
        // rather than in `using` blocks. The finally clause makes a failing assertion mid-way
        // leave nothing activated — double disposal is a documented no-op.
        var activations = new IQueryGuardSessionActivation[sessions.Length];
        try
        {
            for (var i = 0; i < sessions.Length; i++)
            {
                activations[i] = accessor.Activate(sessions[i]);
                Assert.Same(sessions[i], accessor.Current);
            }

            for (var i = sessions.Length - 1; i >= 0; i--)
            {
                activations[i].Dispose();
                var expected = i == 0 ? null : sessions[i - 1];
                Assert.Same(expected, accessor.Current);
            }

            Assert.Equal(0, accessor.OutOfOrderDisposalCount);
        }
        finally
        {
            for (var i = activations.Length - 1; i >= 0; i--)
            {
                activations[i]?.Dispose();
            }
        }
    }

    [Fact]
    public void Disposing_twice_is_safe_and_does_not_pop_the_parent()
    {
        var accessor = new AsyncLocalQueryGuardSessionAccessor();
        var parent = TestData.Session("parent");
        var child = TestData.Session("child");

        using (accessor.Activate(parent))
        {
            using var childActivation = accessor.Activate(child);
            childActivation.Dispose();
            childActivation.Dispose();

            // The second disposal must not walk another level up and silently stop capturing for
            // the parent. The `using` adds a third disposal at scope exit, which must also be inert.
            Assert.Same(parent, accessor.Current);
        }
    }

    [Fact]
    public void Disposing_a_parent_before_its_child_is_detected_rather_than_silently_wrong()
    {
        var accessor = new AsyncLocalQueryGuardSessionAccessor();
        var parent = TestData.Session("parent");
        var child = TestData.Session("child");

        using var parentActivation = accessor.Activate(parent);
        using var childActivation = accessor.Activate(child);

        parentActivation.Dispose();

        Assert.Equal(1, accessor.OutOfOrderDisposalCount);
        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task A_session_flows_across_await_boundaries()
    {
        var accessor = new AsyncLocalQueryGuardSessionAccessor();
        var session = TestData.Session();

        using (accessor.Activate(session))
        {
            await Task.Yield();
            Assert.Same(session, accessor.Current);

            await Task.Delay(1);
            Assert.Same(session, accessor.Current);
        }
    }

    [Fact]
    public async Task A_session_flows_into_fan_out_work_started_inside_the_scope()
    {
        // A single request can start several concurrent EF operations. All of them belong to the
        // request's session.
        var accessor = new AsyncLocalQueryGuardSessionAccessor();
        var session = TestData.Session();

        using (accessor.Activate(session))
        {
            var observed = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => Task.Run(() => accessor.Current)));

            Assert.All(observed, s => Assert.Same(session, s));
        }
    }

    [Fact]
    public async Task A_session_activated_inside_a_task_does_not_escape_to_the_caller()
    {
        // AsyncLocal writes do not propagate back up. This is the property that keeps concurrent
        // requests isolated, so it is worth pinning explicitly rather than relying on it silently.
        var accessor = new AsyncLocalQueryGuardSessionAccessor();

        await Task.Run(() =>
        {
            using var activation = accessor.Activate(TestData.Session("inner"));
            Assert.NotNull(accessor.Current);
        });

        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task Concurrent_flows_never_observe_each_other_session()
    {
        const int FlowCount = 64;
        var accessor = new AsyncLocalQueryGuardSessionAccessor();

        // An asynchronous gate rather than a blocking one: 64 flows waiting on a CountdownEvent
        // would occupy 64 thread-pool threads and make this test depend on pool growth.
        var allArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;

        var tasks = Enumerable.Range(0, FlowCount).Select(index => Task.Run(async () =>
        {
            var session = TestData.Session($"flow-{index}");
            using (accessor.Activate(session))
            {
                // Hold every flow open at the same time so the activations genuinely overlap
                // instead of running one after another.
                if (Interlocked.Increment(ref arrived) == FlowCount)
                {
                    allArrived.SetResult();
                }

                await allArrived.Task;

                for (var i = 0; i < 25; i++)
                {
                    Assert.Same(session, accessor.Current);
                    await Task.Yield();
                }

                return accessor.Current;
            }
        }));

        var results = await Task.WhenAll(tasks);

        Assert.Equal(FlowCount, results.Distinct().Count());
        Assert.Null(accessor.Current);
    }
}
