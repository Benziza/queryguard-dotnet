using System;
using Xunit;

namespace QueryGuard.Tests;

public class QueryFingerprintGroupTests
{
    [Fact]
    public void A_group_always_contains_at_least_one_command()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Group(occurrences: 0));

        Assert.Equal("occurrences", exception.ParamName);
    }

    [Fact]
    public void A_fingerprint_is_required()
        => Assert.Throws<ArgumentNullException>(() => new QueryFingerprintGroup(
            fingerprint: null!,
            occurrences: 1,
            totalDuration: TimeSpan.Zero,
            firstSequence: 1,
            lastSequence: 1,
            kind: QueryCommandKind.Reader));

    [Fact]
    public void Average_duration_is_the_total_divided_by_the_occurrence_count()
    {
        var group = Group(occurrences: 4, totalMs: 10);

        Assert.Equal(TimeSpan.FromMilliseconds(2.5), group.AverageDuration);
    }

    [Fact]
    public void Average_duration_of_a_zero_duration_group_is_zero_rather_than_a_division_error()
    {
        var group = Group(occurrences: 3, totalMs: 0);

        Assert.Equal(TimeSpan.Zero, group.AverageDuration);
    }

    [Fact]
    public void Samples_and_tags_default_to_empty_rather_than_null()
    {
        var group = Group();

        Assert.NotNull(group.Samples);
        Assert.Empty(group.Samples);
        Assert.NotNull(group.Tags);
        Assert.Empty(group.Tags);
    }

    [Fact]
    public void The_sequence_range_shows_whether_the_repetition_was_contiguous()
    {
        // Occurrences packed into a contiguous range look like a loop. The same count spread across
        // the whole session looks more like unrelated repetition, and that distinction is the most
        // useful context a reader gets beyond the count itself.
        var loop = new QueryFingerprintGroup(
            fingerprint: TestData.Fingerprint(),
            occurrences: 10,
            totalDuration: TimeSpan.FromMilliseconds(15),
            firstSequence: 2,
            lastSequence: 11,
            kind: QueryCommandKind.Reader);

        Assert.Equal(2, loop.FirstSequence);
        Assert.Equal(11, loop.LastSequence);
        Assert.Equal(loop.Occurrences, loop.LastSequence - loop.FirstSequence + 1);
    }

    [Fact]
    public void Failed_occurrences_are_counted_within_the_group()
    {
        var group = new QueryFingerprintGroup(
            fingerprint: TestData.Fingerprint(),
            occurrences: 5,
            totalDuration: TimeSpan.FromMilliseconds(5),
            firstSequence: 1,
            lastSequence: 5,
            kind: QueryCommandKind.Reader,
            failureCount: 2);

        Assert.Equal(2, group.FailureCount);
    }

    [Fact]
    public void The_string_representation_leads_with_the_fingerprint_and_the_count()
    {
        var group = Group(occurrences: 51, totalMs: 84.3);

        var text = group.ToString();

        Assert.Contains("QG-FP-1A2B3C4D", text, StringComparison.Ordinal);
        Assert.Contains("x51", text, StringComparison.Ordinal);
    }

    private static QueryFingerprintGroup Group(int occurrences = 3, double totalMs = 4.5)
        => new(
            fingerprint: TestData.Fingerprint(),
            occurrences: occurrences,
            totalDuration: TimeSpan.FromMilliseconds(totalMs),
            firstSequence: 1,
            lastSequence: occurrences,
            kind: QueryCommandKind.Reader);
}
