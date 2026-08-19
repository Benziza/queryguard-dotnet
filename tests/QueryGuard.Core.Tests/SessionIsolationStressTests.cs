using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace QueryGuard.Tests;

/// <summary>
/// Isolation cannot be reasoned into being correct, so it is demonstrated here.
/// </summary>
/// <remarks>
/// <para>
/// Each scope executes a <em>deliberately different</em> number of commands. That is what makes
/// leakage detectable: if every scope ran the same count, a record crossing between them would
/// still produce the expected totals and the bug would pass.
/// </para>
/// <para>
/// These tests are never retried. A flake here is a real defect in either the code or the test,
/// and it is the most expensive class of bug QueryGuard can have — every number it reports depends
/// on this working. See <c>docs/testing-strategy.md</c>.
/// </para>
/// </remarks>
public class SessionIsolationStressTests
{
    private const int Iterations = 3;

    [Theory]
    [InlineData(128)]
    public async Task Parallel_scopes_capture_only_their_own_commands(int scopeCount)
    {
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var accessor = new AsyncLocalQueryGuardSessionAccessor();

            var tasks = Enumerable.Range(0, scopeCount).Select(index => Task.Run(async () =>
            {
                // A distinct expected count per scope, so a stray record changes a total.
                var expectedCommands = (index % 7) + 1;
                var session = TestData.Session($"scope-{index}");
                var fingerprint = TestData.FingerprintFor(index);

                using (accessor.Activate(session))
                {
                    for (var command = 0; command < expectedCommands; command++)
                    {
                        await Task.Yield();

                        var current = accessor.Current;
                        Assert.Same(session, current);

                        current!.Record(
                            QueryCommandKind.Reader,
                            fingerprint,
                            TimeSpan.FromMilliseconds(1));
                    }

                    return (Expected: expectedCommands, Completed: session.Complete(), Fingerprint: fingerprint);
                }
            }));

            var results = await Task.WhenAll(tasks);

            foreach (var (expected, completed, fingerprint) in results)
            {
                Assert.Equal(expected, completed.Records.Count);
                Assert.Equal(0, completed.DroppedRecordCount);

                // Every record must carry this scope's own fingerprint. A leaked record from
                // another scope would show up here even if the counts happened to line up.
                Assert.All(completed.Records, record => Assert.Equal(fingerprint, record.Fingerprint));
            }

            Assert.Null(accessor.Current);
        }
    }

    [Fact]
    public async Task Fan_out_inside_one_scope_records_every_command_exactly_once()
    {
        // A single request can start many concurrent EF operations. All of them belong to that
        // request, and none of them may be lost or double-counted.
        const int FanOut = 64;
        const int CommandsPerBranch = 8;

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var accessor = new AsyncLocalQueryGuardSessionAccessor();
            var session = TestData.Session("fan-out");

            using (accessor.Activate(session))
            {
                await Task.WhenAll(Enumerable.Range(0, FanOut).Select(branch => Task.Run(async () =>
                {
                    for (var command = 0; command < CommandsPerBranch; command++)
                    {
                        await Task.Yield();
                        accessor.Current!.Record(
                            QueryCommandKind.Reader,
                            TestData.FingerprintFor(branch),
                            TimeSpan.FromMilliseconds(1));
                    }
                })));
            }

            var completed = session.Complete();

            Assert.Equal(FanOut * CommandsPerBranch, completed.Records.Count);

            // Sequence numbers are the thing most likely to be corrupted by a race, so assert they
            // form an exact set rather than merely having the right count.
            var sequences = completed.Records.Select(record => record.Sequence).OrderBy(value => value).ToArray();
            Assert.Equal(Enumerable.Range(1, FanOut * CommandsPerBranch), sequences);
        }
    }

    [Fact]
    public async Task Nested_scopes_under_concurrency_attribute_records_to_the_innermost_scope()
    {
        const int OuterCount = 32;

        var accessor = new AsyncLocalQueryGuardSessionAccessor();

        var tasks = Enumerable.Range(0, OuterCount).Select(index => Task.Run(async () =>
        {
            var outer = TestData.Session($"outer-{index}");
            var inner = TestData.Session($"inner-{index}");

            using (accessor.Activate(outer))
            {
                accessor.Current!.Record(QueryCommandKind.Reader, TestData.FingerprintFor(index), TimeSpan.Zero);

                using (accessor.Activate(inner))
                {
                    await Task.Yield();

                    // Two commands in the inner scope, one in the outer, so a mis-attributed record
                    // changes both totals.
                    accessor.Current!.Record(QueryCommandKind.Reader, TestData.FingerprintFor(index), TimeSpan.Zero);
                    accessor.Current!.Record(QueryCommandKind.Scalar, TestData.FingerprintFor(index), TimeSpan.Zero);
                }

                await Task.Yield();
                Assert.Same(outer, accessor.Current);
            }

            return (Outer: outer.Complete(), Inner: inner.Complete());
        }));

        var results = await Task.WhenAll(tasks);

        foreach (var (outer, inner) in results)
        {
            Assert.Single(outer.Records);
            Assert.Equal(2, inner.Records.Count);
        }

        Assert.Equal(0, accessor.OutOfOrderDisposalCount);
    }

    [Fact]
    public async Task Concurrent_completion_and_recording_never_corrupts_a_session()
    {
        // A request can be cancelled or time out while EF operations are still in flight. The
        // session must either record a command or drop it — never produce a torn record or lose
        // count of what happened.
        const int Writers = 16;
        const int WritesPerWriter = 200;

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var session = TestData.Session("racing-completion");
            var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var accepted = new ConcurrentBag<int>();

            var writers = Enumerable.Range(0, Writers).Select(_ => Task.Run(async () =>
            {
                await startGate.Task;

                for (var i = 0; i < WritesPerWriter; i++)
                {
                    var record = session.Record(
                        QueryCommandKind.Reader,
                        TestData.Fingerprint(),
                        TimeSpan.FromMilliseconds(1));

                    if (record is not null)
                    {
                        accepted.Add(record.Sequence);
                    }
                }
            })).ToArray();

            var completer = Task.Run(async () =>
            {
                await startGate.Task;
                await Task.Yield();
                return session.Complete();
            });

            startGate.SetResult();
            await Task.WhenAll(writers);
            var completed = await completer;

            // Refresh the drop count now that every writer has finished.
            completed = session.Complete();

            Assert.Equal(completed.Records.Count, accepted.Count);
            Assert.Equal(accepted.Count, accepted.Distinct().Count());
            Assert.Equal(Writers * WritesPerWriter, accepted.Count + completed.DroppedRecordCount);

            var sequences = completed.Records.Select(record => record.Sequence).OrderBy(value => value).ToArray();
            Assert.Equal(Enumerable.Range(1, completed.Records.Count), sequences);
        }
    }
}
