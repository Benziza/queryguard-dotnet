using Microsoft.Extensions.Logging;

namespace QueryGuard.AspNetCore;

/// <summary>
/// The log event identifiers QueryGuard emits.
/// </summary>
/// <remarks>
/// These are part of the observable contract, not an implementation detail. A team will filter,
/// dashboard, and alert on them, so changing a number is a breaking change to anyone who did. They are
/// grouped in ranges so a filter can select a whole category.
/// </remarks>
public static class QueryGuardEventIds
{
    /// <summary>
    /// One summary per observed request: route, query counts, groups, and database time.
    /// </summary>
    public static readonly EventId RequestSummary = new(1000, nameof(RequestSummary));

    /// <summary>
    /// A repeated-query candidate was found.
    /// </summary>
    public static readonly EventId RepeatedQueryCandidate = new(1001, nameof(RepeatedQueryCandidate));

    /// <summary>
    /// A query budget was exceeded.
    /// </summary>
    public static readonly EventId BudgetExceeded = new(1002, nameof(BudgetExceeded));

    /// <summary>
    /// A finding was suppressed by an allowlist entry or a query tag, and its reason.
    /// </summary>
    public static readonly EventId FindingIgnored = new(1003, nameof(FindingIgnored));

    /// <summary>
    /// A database command failed. Logged as information: the application's own exception is the
    /// authoritative report, and QueryGuard only adds context beside it.
    /// </summary>
    public static readonly EventId CommandFailed = new(1004, nameof(CommandFailed));

    /// <summary>
    /// Commands arrived after the request's session had already completed, and were dropped.
    /// </summary>
    /// <remarks>
    /// Almost always fire-and-forget work started inside a request. Surfaced rather than swallowed,
    /// because a silently truncated report is worse than a noisy one.
    /// </remarks>
    public static readonly EventId RecordsDroppedAfterCompletion = new(1005, nameof(RecordsDroppedAfterCompletion));

    /// <summary>
    /// QueryGuard's own reporting failed.
    /// </summary>
    /// <remarks>
    /// The only event logged at error level, and it never replaces an application failure: a reporter
    /// that throws while an application exception is in flight must not become the exception the user
    /// sees.
    /// </remarks>
    public static readonly EventId ReportingFailed = new(1900, nameof(ReportingFailed));
}
