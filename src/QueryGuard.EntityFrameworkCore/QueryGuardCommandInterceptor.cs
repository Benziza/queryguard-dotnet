using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QueryGuard.EntityFrameworkCore;

/// <summary>
/// Records EF Core relational command execution into the active <see cref="QueryGuardSession"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This interceptor observes and nothing else.</strong> It never modifies the generated SQL,
/// suppresses a command, changes a result, or replaces the exception the application sees. Every
/// override returns the value it was given. A diagnostics tool that changes observed behavior is
/// worse than no tool, because it makes every subsequent debugging session start with "is this real,
/// or is it QueryGuard?".
/// </para>
/// <para>
/// EF Core registers an interceptor as a <strong>singleton</strong> per <c>DbContext</c>
/// configuration, so one instance sees commands from every concurrent request and every parallel
/// test. It therefore holds no per-request state: it asks
/// <see cref="IQueryGuardSessionAccessor"/> which scope the command it is looking at belongs to.
/// With no active scope it does no work at all. See
/// <c>docs/decisions/0002-session-propagation.md</c>.
/// </para>
/// <para>
/// Both the synchronous and asynchronous method pairs are implemented. Implementing only one would
/// produce a tool that silently misses half of a real application's queries — and real ASP.NET Core
/// code is overwhelmingly asynchronous.
/// </para>
/// </remarks>
public sealed class QueryGuardCommandInterceptor : DbCommandInterceptor
{
    private readonly IQueryGuardSessionAccessor _sessionAccessor;
    private readonly IQueryFingerprintFactory _fingerprintFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardCommandInterceptor"/> class.
    /// </summary>
    /// <param name="sessionAccessor">Locates the session the current command belongs to.</param>
    /// <param name="fingerprintFactory">Turns command text into a stable fingerprint.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public QueryGuardCommandInterceptor(
        IQueryGuardSessionAccessor sessionAccessor,
        IQueryFingerprintFactory fingerprintFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionAccessor);
        ArgumentNullException.ThrowIfNull(fingerprintFactory);

        _sessionAccessor = sessionAccessor;
        _fingerprintFactory = fingerprintFactory;
    }

    /// <inheritdoc />
    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Record(command, eventData, QueryCommandKind.Reader);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, QueryCommandKind.Reader);
        return new ValueTask<DbDataReader>(result);
    }

    /// <inheritdoc />
    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        Record(command, eventData, QueryCommandKind.Scalar);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, QueryCommandKind.Scalar);
        return new ValueTask<object?>(result);
    }

    /// <inheritdoc />
    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        Record(command, eventData, QueryCommandKind.NonQuery);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, QueryCommandKind.NonQuery);
        return new ValueTask<int>(result);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A failed command is recorded as evidence and then EF Core continues exactly as it would
    /// have. The original exception keeps its type, message, and stack, and QueryGuard never
    /// becomes the thing that reports the failure.
    /// </remarks>
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
        => RecordFailure(command, eventData);

    /// <inheritdoc />
    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RecordFailure(command, eventData);
        return Task.CompletedTask;
    }

    private void Record(DbCommand command, CommandExecutedEventData eventData, QueryCommandKind kind)
    {
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            // No scope open means no capture. QueryGuard stays silent rather than guessing which
            // scope a command belongs to, and this early return is the whole cost of having the
            // interceptor registered outside a measured scope.
            return;
        }

        Capture(
            session,
            command,
            eventData.CommandSource,
            eventData.Duration,
            Classify(kind, command.CommandText),
            failureType: null);
    }

    private void RecordFailure(DbCommand command, CommandErrorEventData eventData)
    {
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            return;
        }

        Capture(
            session,
            command,
            eventData.CommandSource,
            eventData.Duration,
            // EF Core does not report which execution method was in flight when a command failed,
            // so the statement itself is the only evidence available.
            Classify(QueryCommandKind.Unknown, command.CommandText),
            eventData.Exception?.GetType().FullName);
    }

    private void Capture(
        QueryGuardSession session,
        DbCommand command,
        CommandSource commandSource,
        TimeSpan duration,
        QueryCommandKind kind,
        string? failureType)
    {
        var commandText = command.CommandText;
        var fingerprint = _fingerprintFactory.Create(commandText, kind);

        session.Record(
            kind: kind,
            fingerprint: fingerprint,
            // EF Core measures the command itself and hands the duration over, which is both more
            // accurate than timing around the interceptor and free of correlation state.
            duration: duration < TimeSpan.Zero ? TimeSpan.Zero : duration,
            commandSource: commandSource.ToString(),
            // The count, never the names and never the values.
            // See docs/decisions/0004-parameter-privacy.md.
            parameterCount: command.Parameters.Count,
            isFailed: failureType is not null,
            failureType: failureType,
            tags: QueryGuardQueryTag.Extract(commandText),

            // A callback, not a string. The session invokes it only when capture is enabled and this
            // is the first occurrence of the fingerprint, so with the default configuration no stack
            // is walked, formatted, or allocated on the command path.
            stackTraceProvider: static () => Environment.StackTrace);
    }

    /// <summary>
    /// Decides what a command <em>does</em>, using the execution method EF Core chose and the
    /// statement's leading keyword together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The execution method alone is not enough, because it is provider-dependent in a way that
    /// would quietly corrupt query budgets. On SQLite, EF Core executes
    /// <c>INSERT … RETURNING "Id"</c> through the <em>reader</em> path so it can read the generated
    /// key back. Trusting the method there would count every inserted row against a read budget, and
    /// a budget of ten reads would mean something different on every provider.
    /// </para>
    /// <para>
    /// So a command executed as a reader is demoted to <see cref="QueryCommandKind.NonQuery"/> when
    /// its statement clearly modifies data. The leading keyword is the only signal needed for that —
    /// this is not, and must not become, SQL parsing.
    /// </para>
    /// </remarks>
    private static QueryCommandKind Classify(QueryCommandKind executionKind, string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return executionKind;
        }

        var isModification = IsModificationStatement(commandText);

        return executionKind switch
        {
            // A write executed through the reader path is still a write.
            QueryCommandKind.Reader when isModification => QueryCommandKind.NonQuery,

            // A failure reports no execution method, so the statement is the only evidence.
            QueryCommandKind.Unknown => isModification ? QueryCommandKind.NonQuery : QueryCommandKind.Reader,

            _ => executionKind,
        };
    }

    private static bool IsModificationStatement(string commandText)
    {
        var span = commandText.AsSpan().TrimStart();

        return span.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
            || span.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            || span.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
            || span.StartsWith("MERGE", StringComparison.OrdinalIgnoreCase)
            || span.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)
            || span.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase)
            || span.StartsWith("DROP", StringComparison.OrdinalIgnoreCase);
    }
}
