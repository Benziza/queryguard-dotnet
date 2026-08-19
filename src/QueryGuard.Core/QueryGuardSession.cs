using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace QueryGuard;

/// <summary>
/// The unit QueryGuard measures: one HTTP request, or one integration test.
/// </summary>
/// <remarks>
/// <para>
/// A session has two states. While open it accepts records; once completed it is frozen and
/// exposes an immutable, deterministically ordered view of what it saw. That boundary matters —
/// a session that quietly accepted a late record would produce results that depend on timing,
/// which is the one thing a testing tool cannot afford.
/// </para>
/// <para>
/// Sessions never look up the ambient session themselves. They are handed to the interceptor
/// through <c>IQueryGuardSessionAccessor</c>, which keeps the EF Core interceptor stateless and
/// therefore safe to share as the singleton EF Core registers it as. See
/// <c>docs/decisions/0002-session-propagation.md</c>.
/// </para>
/// </remarks>
public sealed class QueryGuardSession
{
    private readonly List<QueryRecord> _records = [];
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly IQueryGuardRedactor _redactor;

    /// <summary>
    /// Fingerprints already seen, so an optional stack trace is captured for the first occurrence of
    /// each and never for the rest.
    /// </summary>
    private readonly HashSet<string> _seenFingerprints = new(StringComparer.Ordinal);

    /// <summary>
    /// A monotonic starting point, so that a system clock adjustment during a request cannot
    /// produce a negative or wildly wrong duration.
    /// </summary>
    private readonly long _startTimestamp;

    private int _sequence;
    private int _droppedRecordCount;
    private bool _isCompleted;
    private TimeSpan _elapsed;
    private CompletedQueryGuardSession? _completed;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardSession"/> class.
    /// </summary>
    /// <param name="name">
    /// What this session measures: a route pattern such as <c>GET /api/companies</c>, or a test name.
    /// </param>
    /// <param name="policy">The policy this session will be evaluated against.</param>
    /// <param name="redactor">
    /// Supplies the capture settings this session honours — whether a first-occurrence stack trace is
    /// wanted, and how such a trace is filtered. Defaults to a redactor with default options, which
    /// captures no stack traces at all.
    /// </param>
    /// <param name="clock">
    /// An optional wall-clock source, used to make timestamps deterministic in tests. Durations
    /// always come from a monotonic source regardless of this value.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    public QueryGuardSession(
        string name,
        QueryGuardPolicy policy,
        IQueryGuardRedactor? redactor = null,
        Func<DateTimeOffset>? clock = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A session name is required; it identifies the request or test in every report.",
                nameof(name));
        }

        Name = name;
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Id = Guid.NewGuid();
        _redactor = redactor ?? new QueryGuardRedactor();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        StartedAt = _clock();
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Gets what this session measures.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the identifier of this session. Included in reports so records from concurrent
    /// sessions can be told apart during an investigation.
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
    /// Gets a value indicating whether this session has been completed and is now frozen.
    /// </summary>
    public bool IsCompleted
    {
        get
        {
            lock (_gate)
            {
                return _isCompleted;
            }
        }
    }

    /// <summary>
    /// Gets how many commands this session has captured so far.
    /// </summary>
    public int CommandCount
    {
        get
        {
            lock (_gate)
            {
                return _records.Count;
            }
        }
    }

    /// <summary>
    /// Gets how many records arrived after this session was completed.
    /// </summary>
    /// <remarks>
    /// Non-zero means work outlived its scope — most often a fire-and-forget task started inside a
    /// request. Reporters surface it so that the number is a diagnostic rather than a silent loss.
    /// </remarks>
    public int DroppedRecordCount
    {
        get
        {
            lock (_gate)
            {
                return _droppedRecordCount;
            }
        }
    }

    /// <summary>
    /// Gets how long the session was open, or how long it has been open so far.
    /// </summary>
    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
            {
                return _isCompleted ? _elapsed : Stopwatch.GetElapsedTime(_startTimestamp);
            }
        }
    }

    /// <summary>
    /// Records one observed command and assigns it a sequence number.
    /// </summary>
    /// <param name="kind">The kind of relational command.</param>
    /// <param name="fingerprint">The fingerprint of the normalized command text.</param>
    /// <param name="duration">How long the command took.</param>
    /// <param name="commandSource">The EF Core command source, if the provider reported one.</param>
    /// <param name="parameterCount">How many parameters the command declared.</param>
    /// <param name="isFailed">Whether the command failed.</param>
    /// <param name="failureType">The exception type name when the command failed.</param>
    /// <param name="tags">Query tags recognized on the command.</param>
    /// <param name="stackTraceProvider">
    /// Produces a raw stack trace, invoked <strong>only</strong> when stack-trace capture is enabled
    /// and this is the first occurrence of the fingerprint in this session. Passing a callback rather
    /// than a string is what keeps the default configuration free: with capture off, nothing is
    /// walked, formatted, or allocated.
    /// </param>
    /// <returns>
    /// The created record, or <see langword="null"/> when the session is already completed.
    /// </returns>
    /// <remarks>
    /// A late record is dropped rather than throwing. This runs on the application's command path,
    /// and QueryGuard's contract is that observing never changes behavior — throwing here would
    /// turn a diagnostics race into an application failure. The drop is counted in
    /// <see cref="DroppedRecordCount"/> so it is visible rather than silent.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="fingerprint"/> is <see langword="null"/>.</exception>
    public QueryRecord? Record(
        QueryCommandKind kind,
        QueryFingerprint fingerprint,
        TimeSpan duration,
        string? commandSource = null,
        int parameterCount = 0,
        bool isFailed = false,
        string? failureType = null,
        IReadOnlyList<string>? tags = null,
        Func<string?>? stackTraceProvider = null)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        lock (_gate)
        {
            if (_isCompleted)
            {
                _droppedRecordCount++;
                return null;
            }

            var isFirstOccurrence = _seenFingerprints.Add(fingerprint.Id);

            // Bounded to one trace per fingerprint. There is deliberately no configuration that
            // captures a trace per command — that path does not exist in the API.
            // See docs/decisions/0007-stack-trace-policy.md.
            var stackTrace = isFirstOccurrence
                && _redactor.Options.CaptureFirstStackTrace
                && stackTraceProvider is not null
                    ? _redactor.FilterStackTrace(stackTraceProvider())
                    : null;

            var record = new QueryRecord(
                sequence: ++_sequence,
                kind: kind,
                fingerprint: fingerprint,
                duration: duration,
                startedAt: _clock(),
                commandSource: commandSource,
                parameterCount: parameterCount,
                isFailed: isFailed,
                failureType: failureType,
                tags: tags,
                stackTrace: stackTrace);

            _records.Add(record);
            return record;
        }
    }

    /// <summary>
    /// Completes the session and returns an immutable snapshot of what it captured.
    /// </summary>
    /// <returns>The completed session.</returns>
    /// <remarks>
    /// Completion is idempotent: calling it again returns the same snapshot rather than throwing.
    /// The middleware completes in a <c>finally</c> and a test scope completes on disposal, so a
    /// double completion is a normal consequence of an exception unwinding two layers — not a
    /// programming error worth failing a request over.
    /// </remarks>
    public CompletedQueryGuardSession Complete()
    {
        lock (_gate)
        {
            if (_completed is not null)
            {
                // Records that arrive after completion are dropped, and they can only ever arrive
                // after this point, so the snapshot's diagnostic counter is refreshed rather than
                // frozen. The captured commands themselves never change.
                _completed.RefreshDroppedRecordCount(_droppedRecordCount);
                return _completed;
            }

            _elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
            _isCompleted = true;
            _completed = new CompletedQueryGuardSession(
                name: Name,
                id: Id,
                policy: Policy,
                startedAt: StartedAt,
                elapsed: _elapsed,
                records: _records.ToArray(),
                droppedRecordCount: _droppedRecordCount);

            return _completed;
        }
    }

    /// <inheritdoc />
    public override string ToString()
        => $"{Name} ({(IsCompleted ? "completed" : "open")}, {CommandCount} commands)";
}
