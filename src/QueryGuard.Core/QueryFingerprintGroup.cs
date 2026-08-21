using System;
using System.Collections.Generic;

namespace QueryGuard;

/// <summary>
/// Every command in a completed session that shares one <see cref="QueryFingerprint"/>,
/// aggregated.
/// </summary>
/// <remarks>
/// Grouping happens once, when the session completes: never per command. The capture path stays
/// an append so that installing QueryGuard does not change how the application performs.
/// </remarks>
public sealed class QueryFingerprintGroup
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueryFingerprintGroup"/> class.
    /// </summary>
    /// <param name="fingerprint">The shared fingerprint.</param>
    /// <param name="occurrences">How many commands in the session shared it.</param>
    /// <param name="totalDuration">The summed duration of those commands.</param>
    /// <param name="firstSequence">The sequence number of the first occurrence.</param>
    /// <param name="lastSequence">The sequence number of the last occurrence.</param>
    /// <param name="kind">The command kind shared by the group.</param>
    /// <param name="failureCount">How many of the occurrences failed.</param>
    /// <param name="samples">A bounded set of representative records kept as evidence.</param>
    /// <param name="tags">Query tags recognized on any occurrence.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fingerprint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="occurrences"/> is less than one.</exception>
    public QueryFingerprintGroup(
        QueryFingerprint fingerprint,
        int occurrences,
        TimeSpan totalDuration,
        int firstSequence,
        int lastSequence,
        QueryCommandKind kind,
        int failureCount = 0,
        IReadOnlyList<QueryRecord>? samples = null,
        IReadOnlyList<string>? tags = null)
    {
        if (occurrences < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurrences), occurrences, "A group always contains at least one command.");
        }

        Fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
        Occurrences = occurrences;
        TotalDuration = totalDuration;
        FirstSequence = firstSequence;
        LastSequence = lastSequence;
        Kind = kind;
        FailureCount = failureCount;
        Samples = samples ?? Array.Empty<QueryRecord>();
        Tags = tags ?? Array.Empty<string>();
    }

    /// <summary>
    /// Gets the fingerprint shared by every command in this group.
    /// </summary>
    public QueryFingerprint Fingerprint { get; }

    /// <summary>
    /// Gets how many commands in the session shared this fingerprint.
    /// </summary>
    public int Occurrences { get; }

    /// <summary>
    /// Gets the summed duration of every command in this group.
    /// </summary>
    public TimeSpan TotalDuration { get; }

    /// <summary>
    /// Gets the average duration across the group.
    /// </summary>
    public TimeSpan AverageDuration => TimeSpan.FromTicks(TotalDuration.Ticks / Occurrences);

    /// <summary>
    /// Gets the sequence number of the first occurrence.
    /// </summary>
    /// <remarks>
    /// The distance between the first and last sequence numbers is useful context: occurrences
    /// packed into a contiguous range look like a loop, while occurrences spread across the whole
    /// session look more like unrelated repetition.
    /// </remarks>
    public int FirstSequence { get; }

    /// <summary>
    /// Gets the sequence number of the last occurrence.
    /// </summary>
    public int LastSequence { get; }

    /// <summary>
    /// Gets the command kind shared by this group.
    /// </summary>
    public QueryCommandKind Kind { get; }

    /// <summary>
    /// Gets how many occurrences in this group failed.
    /// </summary>
    public int FailureCount { get; }

    /// <summary>
    /// Gets a bounded set of representative records kept as evidence.
    /// </summary>
    /// <remarks>
    /// Bounded on purpose: an endpoint that runs one query ten thousand times must not cause
    /// QueryGuard to retain ten thousand records.
    /// </remarks>
    public IReadOnlyList<QueryRecord> Samples { get; }

    /// <summary>
    /// Gets the first retained stack trace for this group, if capture was enabled.
    /// </summary>
    internal string? FirstCapturedStackTrace
    {
        get
        {
            for (var i = 0; i < Samples.Count; i++)
            {
                if (Samples[i].StackTrace is { } trace)
                {
                    return trace;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the query tags recognized on any occurrence in this group.
    /// </summary>
    public IReadOnlyList<string> Tags { get; }

    /// <inheritdoc />
    public override string ToString()
        => $"{Fingerprint.Id} x{Occurrences} ({TotalDuration.TotalMilliseconds:F2}ms total)";
}
