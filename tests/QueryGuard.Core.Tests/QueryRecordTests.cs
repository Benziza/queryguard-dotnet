using System;
using Xunit;

namespace QueryGuard.Tests;

public class QueryRecordTests
{
    [Fact]
    public void Reader_and_scalar_commands_count_as_reads()
    {
        Assert.True(TestData.Record(kind: QueryCommandKind.Reader).IsRead);
        Assert.True(TestData.Record(kind: QueryCommandKind.Scalar).IsRead);
    }

    [Fact]
    public void Writes_and_unknown_commands_do_not_count_as_reads()
    {
        // A budget of ten reads must mean ten reads regardless of how many entities the endpoint
        // happens to save.
        Assert.False(TestData.Record(kind: QueryCommandKind.NonQuery).IsRead);
        Assert.False(TestData.Record(kind: QueryCommandKind.Unknown).IsRead);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Sequence_numbers_are_one_based(int sequence)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => TestData.Record(sequence: sequence));

        Assert.Equal("sequence", exception.ParamName);
    }

    [Fact]
    public void A_negative_duration_is_rejected()
    {
        // Zero is legitimate for a very fast command. Negative means the caller measured it
        // wrongly, and every number derived from it would be wrong too.
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new QueryRecord(
            sequence: 1,
            kind: QueryCommandKind.Reader,
            fingerprint: TestData.Fingerprint(),
            duration: TimeSpan.FromMilliseconds(-1),
            startedAt: TestData.FixedInstant));

        Assert.Equal("duration", exception.ParamName);
    }

    [Fact]
    public void A_zero_duration_is_accepted()
    {
        var record = TestData.Record(durationMs: 0);

        Assert.Equal(TimeSpan.Zero, record.Duration);
    }

    [Fact]
    public void A_fingerprint_is_required()
        => Assert.Throws<ArgumentNullException>(() => new QueryRecord(
            sequence: 1,
            kind: QueryCommandKind.Reader,
            fingerprint: null!,
            duration: TimeSpan.Zero,
            startedAt: TestData.FixedInstant));

    [Fact]
    public void Tags_default_to_an_empty_collection_rather_than_null()
    {
        var record = TestData.Record();

        Assert.NotNull(record.Tags);
        Assert.Empty(record.Tags);
    }

    [Fact]
    public void A_negative_parameter_count_is_normalized_to_zero()
    {
        // Parameter counts arrive from provider metadata. A nonsensical value should not be able
        // to propagate into a report.
        var record = new QueryRecord(
            sequence: 1,
            kind: QueryCommandKind.Reader,
            fingerprint: TestData.Fingerprint(),
            duration: TimeSpan.Zero,
            startedAt: TestData.FixedInstant,
            parameterCount: -5);

        Assert.Equal(0, record.ParameterCount);
    }

    [Fact]
    public void A_failed_command_records_its_exception_type_but_not_the_exception()
    {
        var record = TestData.Record(isFailed: true);

        Assert.True(record.IsFailed);
        Assert.Equal("Microsoft.Data.Sqlite.SqliteException", record.FailureType);

        // The record deliberately has nowhere to put the exception itself. QueryGuard adds
        // diagnostics alongside a failure; it never becomes the thing that reports it.
        Assert.DoesNotContain(
            typeof(QueryRecord).GetProperties(),
            property => typeof(Exception).IsAssignableFrom(property.PropertyType));
    }
}
