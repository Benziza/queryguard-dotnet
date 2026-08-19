using System;
using Xunit;

namespace QueryGuard.Tests;

public class QueryGuardSessionTests
{
    [Fact]
    public void A_new_session_is_open_and_empty()
    {
        var session = TestData.Session();

        Assert.False(session.IsCompleted);
        Assert.Equal(0, session.CommandCount);
        Assert.Equal(0, session.DroppedRecordCount);
        Assert.NotEqual(Guid.Empty, session.Id);
    }

    [Fact]
    public void A_session_name_is_required()
    {
        var policy = QueryGuardPolicy.Create("test");

        Assert.Throws<ArgumentException>(() => new QueryGuardSession("   ", policy));
        Assert.Throws<ArgumentException>(() => new QueryGuardSession(string.Empty, policy));
    }

    [Fact]
    public void A_policy_is_required()
        => Assert.Throws<ArgumentNullException>(() => new QueryGuardSession("test", null!));

    [Fact]
    public void Records_are_assigned_monotonic_one_based_sequence_numbers()
    {
        var session = TestData.Session();

        var first = session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.Zero);
        var second = session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.Zero);
        var third = session.Record(QueryCommandKind.Scalar, TestData.Fingerprint("DEADBEEF"), TimeSpan.Zero);

        Assert.Equal(1, first!.Sequence);
        Assert.Equal(2, second!.Sequence);
        Assert.Equal(3, third!.Sequence);
        Assert.Equal(3, session.CommandCount);
    }

    [Fact]
    public void A_completed_session_stops_accepting_records()
    {
        var session = TestData.Session();
        session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.Zero);

        var completed = session.Complete();

        Assert.True(session.IsCompleted);
        Assert.Single(completed.Records);

        var late = session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.Zero);

        Assert.Null(late);
        Assert.Single(completed.Records);
    }

    [Fact]
    public void A_late_record_is_counted_rather_than_thrown()
    {
        // This runs on the application's command path. Throwing would turn a diagnostics race
        // into an application failure, so the drop is counted and surfaced instead.
        var session = TestData.Session();
        session.Complete();

        session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.Zero);
        session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.Zero);

        Assert.Equal(2, session.DroppedRecordCount);

        // A drop can only ever happen after completion, so the snapshot has to be able to report a
        // count that did not exist when it was created. Everything else about it stays frozen.
        Assert.Equal(2, session.Complete().DroppedRecordCount);
    }

    [Fact]
    public void Refreshing_the_dropped_count_does_not_change_the_captured_commands()
    {
        var session = TestData.Session();
        session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.FromMilliseconds(3));

        var completed = session.Complete();
        var totalDuration = completed.TotalDatabaseDuration;

        session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.FromMilliseconds(99));
        var refreshed = session.Complete();

        Assert.Same(completed, refreshed);
        Assert.Single(refreshed.Records);
        Assert.Equal(totalDuration, refreshed.TotalDatabaseDuration);
        Assert.Equal(1, refreshed.DroppedRecordCount);
    }

    [Fact]
    public void Completion_is_idempotent_and_returns_the_same_snapshot()
    {
        // The middleware completes in a finally and a test scope completes on disposal, so a
        // double completion is a normal consequence of an exception unwinding two layers.
        var session = TestData.Session();
        session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.Zero);

        var first = session.Complete();
        var second = session.Complete();

        Assert.Same(first, second);
    }

    [Fact]
    public void A_completed_snapshot_is_not_affected_by_later_activity()
    {
        var session = TestData.Session();
        session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.Zero);

        var completed = session.Complete();
        var recordsAtCompletion = completed.Records.Count;

        session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.Zero);

        Assert.Equal(recordsAtCompletion, completed.Records.Count);
    }

    [Fact]
    public void A_fingerprint_is_required_to_record()
    {
        var session = TestData.Session();

        Assert.Throws<ArgumentNullException>(
            () => session.Record(QueryCommandKind.Reader, null!, TimeSpan.Zero));
    }

    [Fact]
    public void Elapsed_time_keeps_growing_while_the_session_is_open_and_freezes_on_completion()
    {
        var session = TestData.Session();

        var completed = session.Complete();
        var frozen = session.Elapsed;

        Assert.Equal(frozen, session.Elapsed);
        Assert.Equal(frozen, completed.Elapsed);
        Assert.True(frozen >= TimeSpan.Zero);
    }

    [Fact]
    public void The_snapshot_counts_only_the_command_kinds_the_policy_counts()
    {
        var policy = QueryGuardPolicy.Create("reads-only")
            .WithCountedKinds(QueryCommandKind.Reader);
        var session = TestData.Session(policy: policy);

        session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.Zero);
        session.Record(QueryCommandKind.Scalar, TestData.Fingerprint("00000002"), TimeSpan.Zero);
        session.Record(QueryCommandKind.NonQuery, TestData.Fingerprint("00000003"), TimeSpan.Zero);

        var completed = session.Complete();

        Assert.Equal(3, completed.Records.Count);
        Assert.Equal(1, completed.CountedCommandCount);
    }

    [Fact]
    public void The_snapshot_sums_database_duration_across_every_command_kind()
    {
        var session = TestData.Session();

        session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.FromMilliseconds(10));
        session.Record(QueryCommandKind.NonQuery, TestData.Fingerprint("00000002"), TimeSpan.FromMilliseconds(5));

        var completed = session.Complete();

        Assert.Equal(TimeSpan.FromMilliseconds(15), completed.TotalDatabaseDuration);
    }

    [Fact]
    public void The_snapshot_counts_failed_commands()
    {
        var session = TestData.Session();

        session.Record(QueryCommandKind.Reader, TestData.Fingerprint(), TimeSpan.Zero);
        session.Record(
            QueryCommandKind.Reader,
            TestData.Fingerprint(),
            TimeSpan.Zero,
            isFailed: true,
            failureType: "Microsoft.Data.Sqlite.SqliteException");

        var completed = session.Complete();

        Assert.Equal(1, completed.FailedCommandCount);
    }
}
