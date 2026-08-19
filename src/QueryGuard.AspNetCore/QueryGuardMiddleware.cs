using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QueryGuard.AspNetCore;

/// <summary>
/// Opens a QueryGuard session around each request and reports what it recorded.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This middleware observes.</strong> It does not write to the response body, add headers, or
/// throw on the request path. The response the application produces and the exception it raises are
/// exactly what a client sees, with QueryGuard enabled or disabled — there is a test that runs the same
/// request both ways and compares. See <c>docs/decisions/0006-aspnet-observe-only.md</c>.
/// </para>
/// <para>
/// The session is completed in a <c>finally</c>, so a request that throws still produces a report. A
/// budget failure changes what is logged and nothing else; failing a build is what the testing API is
/// for.
/// </para>
/// </remarks>
public sealed class QueryGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IQueryGuardSessionAccessor _sessionAccessor;
    private readonly QueryGuardAnalyzer _analyzer;
    private readonly IQueryGuardRedactor _redactor;
    private readonly IOptionsMonitor<QueryGuardOptions> _options;
    private readonly ILogger<QueryGuardMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="sessionAccessor">Makes the request's session current for the interceptor to find.</param>
    /// <param name="analyzer">Turns the completed session into a result.</param>
    /// <param name="redactor">Supplies the capture settings the session honours.</param>
    /// <param name="options">The QueryGuard options, monitored so configuration reloads take effect.</param>
    /// <param name="logger">Receives the summary and findings.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public QueryGuardMiddleware(
        RequestDelegate next,
        IQueryGuardSessionAccessor sessionAccessor,
        QueryGuardAnalyzer analyzer,
        IQueryGuardRedactor redactor,
        IOptionsMonitor<QueryGuardOptions> options,
        ILogger<QueryGuardMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(sessionAccessor);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _next = next;
        _sessionAccessor = sessionAccessor;
        _analyzer = analyzer;
        _redactor = redactor;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Observes one request.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task that completes when the rest of the pipeline has.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = _options.CurrentValue;

        if (!options.Enabled || options.IsExcluded(context.Request.Path.Value))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var scopeName = QueryGuardRouteName.Resolve(context);
        var policy = options.ResolvePolicy(scopeName);
        var session = new QueryGuardSession(scopeName, policy, _redactor);

        using var activation = _sessionAccessor.Activate(session);

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            // In a finally, so a request that throws still produces a report — the failing request is
            // usually the interesting one. Report() never throws, so it cannot replace the
            // application's exception on the way out.
            Report(session, context);
        }
    }

    private void Report(QueryGuardSession session, HttpContext context)
    {
        try
        {
            var completed = session.Complete();
            var result = _analyzer.Analyze(completed);
            var options = _options.CurrentValue;

            if (result.Findings.Count == 0 && !options.LogSummaryWhenClean)
            {
                // QueryGuard runs on every request. Logging a clean summary each time is noise that
                // trains people to filter QueryGuard out of their logs entirely.
                return;
            }

            LogSummary(result, context);

            for (var i = 0; i < result.Findings.Count; i++)
            {
                LogFinding(result.Findings[i]);
            }

            // Read from the session rather than the result: a dropped record is by definition observed
            // after completion, so the snapshot's counter is the live one.
            if (completed.DroppedRecordCount > 0)
            {
                _logger.LogWarning(
                    QueryGuardEventIds.RecordsDroppedAfterCompletion,
                    "QueryGuard dropped {DroppedCount} commands that completed after {Route} finished. This usually means fire-and-forget work started inside the request.",
                    completed.DroppedRecordCount,
                    result.SessionName);
            }
        }
        catch (Exception exception)
        {
            // A diagnostics tool must never become the reason a request fails. If reporting is broken,
            // that is QueryGuard's problem to surface, not the application's problem to propagate.
            _logger.LogError(
                QueryGuardEventIds.ReportingFailed,
                exception,
                "QueryGuard failed to report on {Route}. The request itself was unaffected.",
                session.Name);
        }
    }

    private void LogSummary(QueryGuardResult result, HttpContext context)
    {
        var level = result.IsSuccess ? LogLevel.Information : LogLevel.Warning;

        // Guarded because this runs once per observed request. Computing the summary arguments for a
        // level nobody is listening to is work done for nothing, on every request.
        if (!_logger.IsEnabled(level))
        {
            return;
        }

        _logger.Log(
            level,
            QueryGuardEventIds.RequestSummary,
            "QueryGuard {Route} -> {StatusCode}: {ReadQueries} read queries in {GroupCount} groups, {DatabaseMs:F1} ms database time, {FailureCount} failures, {WarningCount} warnings, {IgnoredCount} ignored.",
            result.SessionName,
            context.Response.StatusCode,
            result.ReadCommandCount,
            result.Groups.Count,
            result.TotalDatabaseDuration.TotalMilliseconds,
            result.FailureCount,
            result.WarningCount,
            result.IgnoredFindingCount);
    }

    private void LogFinding(QueryFinding finding)
    {
        if (finding.IsIgnored)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    QueryGuardEventIds.FindingIgnored,
                    "QueryGuard ignored {Rule} for {Fingerprint}: {Reason}",
                    finding.RuleName,
                    finding.Fingerprint?.Id ?? "(session)",
                    finding.IgnoreReason);
            }

            return;
        }

        var eventId = finding.Kind == QueryFindingKind.RepeatedQueryCandidate
            ? QueryGuardEventIds.RepeatedQueryCandidate
            : finding.Kind == QueryFindingKind.CommandFailure
                ? QueryGuardEventIds.CommandFailed
                : QueryGuardEventIds.BudgetExceeded;

        var level = finding.Severity switch
        {
            // Deliberately not Error. An application's query count exceeding a budget is not
            // QueryGuard malfunctioning, and Error is reserved for QueryGuard's own failures so that
            // alerting on it stays meaningful.
            QueryGuardSeverity.Failure => LogLevel.Warning,
            QueryGuardSeverity.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };

        if (!_logger.IsEnabled(level))
        {
            return;
        }

        _logger.Log(
            level,
            eventId,
            "QueryGuard {Severity} {Rule}: {Message}",
            finding.Severity,
            finding.RuleName,
            finding.Message);

        // Evidence is emitted as separate entries rather than one multi-line message so a structured
        // sink keeps each line queryable instead of storing one opaque blob.
        for (var i = 0; i < finding.Evidence.Count; i++)
        {
            _logger.Log(level, eventId, "  {Evidence}", finding.Evidence[i]);
        }

        if (finding.StackTrace is { } stackTrace)
        {
            _logger.Log(level, eventId, "  First occurrence at:{NewLine}{StackTrace}", Environment.NewLine, stackTrace);
        }
    }
}
