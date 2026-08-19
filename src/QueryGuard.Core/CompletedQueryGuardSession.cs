using System;
using System.Collections.Generic;

namespace QueryGuard;

/// <summary>
/// A frozen snapshot of everything one session captured.
/// </summary>
/// <remarks>
/// Separating this from <see cref="QueryGuardSession"/> is what makes "a completed session cannot
/// change" a type-level guarantee rather than a convention. Policy evaluation, assertions, and
/// reporters all consume this type, so none of them can accidentally mutate what they are
/// reporting on.
/// </remarks>
public sealed class CompletedQueryGuardSession
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompletedQueryGuardSession"/> class.
    /// </summary>
    /// <param name="name">What the session measured.</param>
    /// <param name="id">The session identifier.</param>
    /// <param name="policy">The policy the session is evaluated against.</param>
    /// <param name="startedAt">When the session opened, in UTC.</param>
    /// <param name="elapsed">How long the session was open.</param>
    /// <param name="records">The captured commands, in capture order.</param>
    /// <param name="droppedRecordCount">How many records arrived after completion.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public CompletedQueryGuardSession(
        string name,
        Guid id,
        QueryGuardPolicy policy,
        DateTimeOffset startedAt,
        TimeSpan elapsed,
        IReadOnlyList<QueryRecord> records,
        int droppedRecordCount = 0)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Id = id;
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        StartedAt = startedAt;
        Elapsed = elapsed;
        Records = records ?? throw new ArgumentNullException(nameof(records));
        DroppedRecordCount = droppedRecordCount;

        var totalDatabaseDuration = TimeSpan.Zero;
        var countedCommandCount = 0;
        var failedCommandCount = 0;

        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            totalDatabaseDuration += record.Duration;

            if (policy.Counts(record.Kind))
            {
                countedCommandCount++;
            }

            if (record.IsFailed)
            {
                failedCommandCount++;
            }
        }

        TotalDatabaseDuration = totalDatabaseDuration;
        CountedCommandCount = countedCommandCount;
        FailedCommandCount = failedCommandCount;
    }

    /// <summary>
    /// Gets what the session measured: a route pattern or a test name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the session identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the policy this session is evaluated against.
    /// </summary>
    public QueryGuardPolicy Policy { get; }

    /// <summary>
    /// Gets when the session opened, in UTC.
    /// </summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Gets how long the session was open, measured with a monotonic clock.
    /// </summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// Gets the captured commands, in capture order and therefore in sequence order.
    /// </summary>
    public IReadOnlyList<QueryRecord> Records { get; }

    /// <summary>
    /// Gets how many records arrived after the session was completed and were therefore dropped.
    /// </summary>
    /// <remarks>
    /// This is the one value on an otherwise frozen snapshot that can change after construction,
    /// and it has to be: a dropped record is by definition observed <em>after</em> completion, so a
    /// value captured at completion time would always be zero and the diagnostic would be
    /// worthless. <see cref="QueryGuardSession.Complete"/> refreshes it on every call. The captured
    /// commands, and therefore every number derived from them, stay immutable.
    /// </remarks>
    public int DroppedRecordCount { get; private set; }

    /// <summary>
    /// Updates the count of records that arrived after completion.
    /// </summary>
    /// <param name="droppedRecordCount">The current count.</param>
    internal void RefreshDroppedRecordCount(int droppedRecordCount)
        => DroppedRecordCount = droppedRecordCount;

    /// <summary>
    /// Gets how many captured commands count toward this session's policy budgets.
    /// </summary>
    public int CountedCommandCount { get; }

    /// <summary>
    /// Gets how many captured commands failed.
    /// </summary>
    public int FailedCommandCount { get; }

    /// <summary>
    /// Gets the summed duration of every captured command.
    /// </summary>
    public TimeSpan TotalDatabaseDuration { get; }

    /// <inheritdoc />
    public override string ToString()
        => $"{Name}: {Records.Count} commands ({CountedCommandCount} counted) in {Elapsed.TotalMilliseconds:F1}ms";
}
