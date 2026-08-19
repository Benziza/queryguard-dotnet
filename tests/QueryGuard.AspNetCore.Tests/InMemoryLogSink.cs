using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace QueryGuard.AspNetCore.Tests;

/// <summary>
/// Captures log entries so tests can assert on event IDs, levels, and message content.
/// </summary>
/// <remarks>
/// Hand-written rather than pulled from a package: the only alternative that covers both target
/// frameworks would add a production-adjacent dependency for something a screenful of code does. Log
/// output is part of QueryGuard's observable contract, so it deserves real assertions.
/// </remarks>
internal sealed class InMemoryLogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    internal IReadOnlyList<LogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);

    public void Dispose()
    {
        // Nothing to release; the queue lives as long as the test.
    }

    internal void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Finds QueryGuard's entries with a given event ID.
    /// </summary>
    /// <remarks>
    /// Filtered by category as well as by identifier, because event IDs are only unique within a
    /// category. ASP.NET Core's routing middleware also logs 1000 and 1001, so matching on the number
    /// alone silently mixes framework entries into an assertion about QueryGuard.
    /// </remarks>
    internal IEnumerable<LogEntry> WithEventId(EventId eventId)
        => FromQueryGuard().Where(entry => entry.EventId.Id == eventId.Id);

    internal bool HasEventId(EventId eventId) => WithEventId(eventId).Any();

    internal IEnumerable<LogEntry> FromQueryGuard()
        => Entries.Where(entry => entry.Category.StartsWith("QueryGuard", StringComparison.Ordinal));

    private void Add(LogEntry entry) => _entries.Enqueue(entry);

    internal sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception);

    private sealed class Sink : ILogger
    {
        private readonly InMemoryLogSink _owner;
        private readonly string _category;

        internal Sink(InMemoryLogSink owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _owner.Add(new LogEntry(_category, logLevel, eventId, formatter(state, exception), exception));

        private sealed class NullScope : IDisposable
        {
            internal static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
