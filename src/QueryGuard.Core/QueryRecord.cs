using System;
using System.Collections.Generic;

namespace QueryGuard;

/// <summary>
/// One relational command observed inside a <see cref="QueryGuardSession"/>.
/// </summary>
/// <remarks>
/// <para>
/// What this type does <em>not</em> carry is as deliberate as what it does. There is no field
/// for a parameter value and no field for a connection string anywhere in the model, so no
/// reporter — present or future — can leak data that was never captured. Parameter
/// <em>names</em> are counted rather than stored, because the count is occasionally useful
/// evidence while the names are not worth the risk. See
/// <c>docs/decisions/0004-parameter-privacy.md</c>.
/// </para>
/// <para>
/// Records are immutable and carry a monotonic <see cref="Sequence"/> so that ordering is
/// deterministic. Snapshot tests and CI output depend on two identical runs producing identical
/// results.
/// </para>
/// </remarks>
public sealed class QueryRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueryRecord"/> class.
    /// </summary>
    /// <param name="sequence">The one-based position of this command within its session.</param>
    /// <param name="kind">The kind of relational command.</param>
    /// <param name="fingerprint">The fingerprint of the normalized command text.</param>
    /// <param name="duration">How long the command took to execute.</param>
    /// <param name="startedAt">When the command started, in UTC.</param>
    /// <param name="commandSource">
    /// The EF Core command source, for example <c>Linq</c> or <c>SaveChanges</c>, or
    /// <see langword="null"/> when the provider did not report one.
    /// </param>
    /// <param name="parameterCount">How many parameters the command declared.</param>
    /// <param name="isFailed">Whether the command completed by failing.</param>
    /// <param name="failureType">
    /// The exception type name when <paramref name="isFailed"/> is <see langword="true"/>.
    /// The exception itself is never captured, and never replaces the one the application sees.
    /// </param>
    /// <param name="tags">Query tags recognized on the command, if any.</param>
    /// <param name="stackTrace">
    /// A filtered stack trace for the first occurrence of this fingerprint, when capture is enabled.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sequence"/> is less than one.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="fingerprint"/> is <see langword="null"/>.</exception>
    public QueryRecord(
        int sequence,
        QueryCommandKind kind,
        QueryFingerprint fingerprint,
        TimeSpan duration,
        DateTimeOffset startedAt,
        string? commandSource = null,
        int parameterCount = 0,
        bool isFailed = false,
        string? failureType = null,
        IReadOnlyList<string>? tags = null,
        string? stackTrace = null)
    {
        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence), sequence, "Sequence numbers are one-based within a session.");
        }

        // A monotonic clock can report zero elapsed time for a very fast command, but a negative
        // duration means the caller measured it wrongly, and every number derived from it would
        // be wrong too.
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration), duration, "A command duration cannot be negative.");
        }

        Sequence = sequence;
        Kind = kind;
        Fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
        Duration = duration;
        StartedAt = startedAt;
        CommandSource = commandSource;
        ParameterCount = parameterCount < 0 ? 0 : parameterCount;
        IsFailed = isFailed;
        FailureType = failureType;
        Tags = tags ?? Array.Empty<string>();
        StackTrace = stackTrace;
    }

    /// <summary>
    /// Gets the one-based position of this command within its session.
    /// </summary>
    /// <remarks>
    /// Sequence numbers make "the first occurrence of this fingerprint" meaningful even when
    /// commands complete out of order because of request fan-out.
    /// </remarks>
    public int Sequence { get; }

    /// <summary>
    /// Gets the kind of relational command.
    /// </summary>
    public QueryCommandKind Kind { get; }

    /// <summary>
    /// Gets the fingerprint of the normalized command text.
    /// </summary>
    public QueryFingerprint Fingerprint { get; }

    /// <summary>
    /// Gets how long the command took to execute, measured with a monotonic clock.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets when the command started, in UTC.
    /// </summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Gets the EF Core command source, or <see langword="null"/> when the provider did not
    /// report one.
    /// </summary>
    public string? CommandSource { get; }

    /// <summary>
    /// Gets how many parameters the command declared. The names and values are not captured.
    /// </summary>
    public int ParameterCount { get; }

    /// <summary>
    /// Gets a value indicating whether the command completed by failing.
    /// </summary>
    /// <remarks>
    /// A failed command is recorded as evidence. The original exception is never captured,
    /// wrapped, or replaced — it propagates to the application untouched.
    /// </remarks>
    public bool IsFailed { get; }

    /// <summary>
    /// Gets the exception type name for a failed command, or <see langword="null"/> otherwise.
    /// </summary>
    public string? FailureType { get; }

    /// <summary>
    /// Gets the query tags recognized on this command.
    /// </summary>
    /// <remarks>
    /// Only tags QueryGuard understands are retained. An arbitrary <c>TagWith</c> comment is
    /// stripped during normalization like any other comment.
    /// </remarks>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// Gets a filtered stack trace for the first occurrence of this fingerprint, or
    /// <see langword="null"/> when capture is disabled or this is not the first occurrence.
    /// </summary>
    /// <remarks>
    /// "Where is this coming from?" is the first question anyone asks after seeing a finding, and this
    /// is the only way to answer it from inside an interceptor. It is also expensive per command, so it
    /// is off by default and bounded to one trace per fingerprint when enabled. Framework frames are
    /// filtered out, leaving the application code that is actually actionable. See
    /// <c>docs/decisions/0007-stack-trace-policy.md</c>.
    /// </remarks>
    public string? StackTrace { get; }

    /// <summary>
    /// Gets a value indicating whether this command counts toward read-query budgets.
    /// </summary>
    public bool IsRead => Kind is QueryCommandKind.Reader or QueryCommandKind.Scalar;

    /// <inheritdoc />
    public override string ToString()
        => $"#{Sequence} {Kind} {Fingerprint.Id} {Duration.TotalMilliseconds:F2}ms"
            + (IsFailed ? " (failed)" : string.Empty);
}
