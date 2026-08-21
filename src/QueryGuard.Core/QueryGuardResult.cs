using System;
using System.Collections.Generic;

namespace QueryGuard;

/// <summary>
/// The immutable outcome of evaluating one completed session against its policy.
/// </summary>
/// <remarks>
/// <para>
/// This is the object every reporter serializes and every assertion inspects, so its ordering is
/// deterministic: two identical runs produce identical output. Without that, snapshot tests are
/// worthless and CI diffs become noise.
/// </para>
/// <para>
/// It is also already redacted. The central privacy policy runs before a result is constructed,
/// so a reporter cannot leak anything by accident and adding a new reporter cannot introduce a
/// leak. See <c>docs/decisions/0004-parameter-privacy.md</c>.
/// </para>
/// </remarks>
public sealed class QueryGuardResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardResult"/> class.
    /// </summary>
    /// <param name="sessionName">The name of the session that was evaluated.</param>
    /// <param name="sessionId">The identifier of the session that was evaluated.</param>
    /// <param name="policyName">The name of the policy it was evaluated against.</param>
    /// <param name="startedAt">When the session opened, in UTC.</param>
    /// <param name="elapsed">How long the session was open.</param>
    /// <param name="records">Every captured command, ordered by sequence number.</param>
    /// <param name="groups">Fingerprint groups, ordered by descending occurrence count.</param>
    /// <param name="findings">Findings, ordered by descending severity then by rule name.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public QueryGuardResult(
        string sessionName,
        Guid sessionId,
        string policyName,
        DateTimeOffset startedAt,
        TimeSpan elapsed,
        IReadOnlyList<QueryRecord> records,
        IReadOnlyList<QueryFingerprintGroup> groups,
        IReadOnlyList<QueryFinding> findings)
    {
        SessionName = sessionName ?? throw new ArgumentNullException(nameof(sessionName));
        SessionId = sessionId;
        PolicyName = policyName ?? throw new ArgumentNullException(nameof(policyName));
        StartedAt = startedAt;
        Elapsed = elapsed;
        Records = records ?? throw new ArgumentNullException(nameof(records));
        Groups = groups ?? throw new ArgumentNullException(nameof(groups));
        Findings = findings ?? throw new ArgumentNullException(nameof(findings));

        var totalDatabaseDuration = TimeSpan.Zero;
        var readCount = 0;
        for (var i = 0; i < records.Count; i++)
        {
            totalDatabaseDuration += records[i].Duration;
            if (records[i].IsRead)
            {
                readCount++;
            }
        }

        TotalDatabaseDuration = totalDatabaseDuration;
        ReadCommandCount = readCount;

        var failureCount = 0;
        var warningCount = 0;
        var ignoredCount = 0;
        for (var i = 0; i < findings.Count; i++)
        {
            var finding = findings[i];
            if (finding.IsIgnored)
            {
                ignoredCount++;
                continue;
            }

            switch (finding.Severity)
            {
                case QueryGuardSeverity.Failure:
                    failureCount++;
                    break;
                case QueryGuardSeverity.Warning:
                    warningCount++;
                    break;
                default:
                    break;
            }
        }

        FailureCount = failureCount;
        WarningCount = warningCount;
        IgnoredFindingCount = ignoredCount;
    }

    /// <summary>
    /// Gets the name of the session that was evaluated, such as a route pattern or a test name.
    /// </summary>
    public string SessionName { get; }

    /// <summary>
    /// Gets the identifier of the session that was evaluated.
    /// </summary>
    public Guid SessionId { get; }

    /// <summary>
    /// Gets the name of the policy the session was evaluated against.
    /// </summary>
    public string PolicyName { get; }

    /// <summary>
    /// Gets when the session opened, in UTC.
    /// </summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Gets how long the session was open. This is wall-clock time for the request or test, not
    /// database time: compare it against <see cref="TotalDatabaseDuration"/>.
    /// </summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// Gets every captured command, ordered by sequence number.
    /// </summary>
    public IReadOnlyList<QueryRecord> Records { get; }

    /// <summary>
    /// Gets the fingerprint groups, ordered by descending occurrence count so the most repeated
    /// query is first.
    /// </summary>
    public IReadOnlyList<QueryFingerprintGroup> Groups { get; }

    /// <summary>
    /// Gets the findings, ordered by descending severity and then by rule name.
    /// </summary>
    /// <remarks>
    /// Ignored findings are included. Removing them would make an allowlist a place to hide
    /// problems rather than a place to document them.
    /// </remarks>
    public IReadOnlyList<QueryFinding> Findings { get; }

    /// <summary>
    /// Gets the total number of captured commands, of every kind.
    /// </summary>
    public int TotalCommandCount => Records.Count;

    /// <summary>
    /// Gets the number of captured read commands, meaning reader and scalar commands.
    /// </summary>
    public int ReadCommandCount { get; }

    /// <summary>
    /// Gets the summed duration of every captured command.
    /// </summary>
    public TimeSpan TotalDatabaseDuration { get; }

    /// <summary>
    /// Gets the number of findings that cause this result to fail.
    /// </summary>
    public int FailureCount { get; }

    /// <summary>
    /// Gets the number of warning findings that were not ignored.
    /// </summary>
    public int WarningCount { get; }

    /// <summary>
    /// Gets the number of findings suppressed by an allowlist entry.
    /// </summary>
    public int IgnoredFindingCount { get; }

    /// <summary>
    /// Gets a value indicating whether the session satisfied its policy.
    /// </summary>
    /// <remarks>
    /// Warnings do not affect this. A warning is evidence worth reading; a failure is a verdict.
    /// </remarks>
    public bool IsSuccess => FailureCount == 0;

    /// <summary>
    /// Gets the fingerprint group with the most occurrences, or <see langword="null"/> when no
    /// commands were captured.
    /// </summary>
    /// <remarks>
    /// This is what a failure message leads with: of everything QueryGuard saw, this group is the
    /// most likely to be the actual problem.
    /// </remarks>
    public QueryFingerprintGroup? TopRepeatedGroup => Groups.Count > 0 ? Groups[0] : null;

    /// <inheritdoc />
    public override string ToString()
        => $"{SessionName}: {ReadCommandCount} read queries, {Groups.Count} groups, "
            + $"{FailureCount} failures, {WarningCount} warnings"
            + (IgnoredFindingCount > 0 ? $", {IgnoredFindingCount} ignored" : string.Empty);
}
