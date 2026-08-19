using System;
using System.Collections.Generic;
using System.Globalization;

namespace QueryGuard;

/// <summary>
/// Evaluates a policy's budgets against a completed session's groups and produces findings.
/// </summary>
/// <remarks>
/// <para>
/// Split from <see cref="QueryGuardAnalyzer"/> because these are two different jobs. Grouping asks
/// "what happened?" and has one right answer. Budgets ask "is that acceptable?" and the answer is
/// whatever the user configured — so the rules live where they can be read as a list, and each one
/// produces a finding carrying the numbers that justify it.
/// </para>
/// <para>
/// Every rule is opt-in. A policy with no budgets configured produces no budget findings at all,
/// which is what makes installing QueryGuard safe: it reports what it sees before it starts failing
/// anything.
/// </para>
/// </remarks>
public sealed class QueryBudgetEvaluator : IQueryBudgetEvaluator
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public IReadOnlyList<QueryFinding> Evaluate(
        CompletedQueryGuardSession session,
        IReadOnlyList<QueryFingerprintGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(groups);

        var policy = session.Policy;
        var findings = new List<QueryFinding>();

        EvaluateTotalQueryBudget(findings, session, policy);
        EvaluateFingerprintOccurrenceBudget(findings, groups, policy);
        EvaluateDuplicateGroupBudget(findings, groups, policy);
        EvaluateTotalDurationBudget(findings, session, policy);
        EvaluateSlowQueries(findings, session, policy);
        EvaluateCommandFailures(findings, session);

        return findings;
    }

    private static void EvaluateTotalQueryBudget(
        List<QueryFinding> findings,
        CompletedQueryGuardSession session,
        QueryGuardPolicy policy)
    {
        if (policy.MaxQueries is not { } budget)
        {
            return;
        }

        var actual = session.CountedCommandCount;

        // Exactly at the budget passes. A budget is a maximum, and an off-by-one here would mean
        // every user's carefully chosen number is quietly one lower than they wrote.
        if (actual <= budget)
        {
            return;
        }

        findings.Add(new QueryFinding(
            kind: QueryFindingKind.TotalQueryBudget,
            severity: policy.MaxQueriesSeverity,
            message: string.Create(
                CultureInfo.InvariantCulture,
                $"{session.Name} executed {actual} queries; the budget is {budget}."),
            ruleName: RuleNames.MaxQueries,
            expected: budget,
            actual: actual,
            evidence:
            [
                string.Create(CultureInfo.InvariantCulture, $"Counted commands: {actual} (budget: {budget})"),
                string.Create(CultureInfo.InvariantCulture, $"Total commands including writes: {session.Records.Count}"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Total database time: {session.TotalDatabaseDuration.TotalMilliseconds:F1} ms"),
            ]));
    }

    private static void EvaluateFingerprintOccurrenceBudget(
        List<QueryFinding> findings,
        IReadOnlyList<QueryFingerprintGroup> groups,
        QueryGuardPolicy policy)
    {
        if (policy.MaxOccurrencesPerFingerprint is not { } budget)
        {
            return;
        }

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.Occurrences <= budget)
            {
                continue;
            }

            var ignoreReason = QueryGuardAnalyzer.ResolveIgnoreReason(policy, group);

            findings.Add(new QueryFinding(
                kind: QueryFindingKind.FingerprintOccurrenceBudget,
                severity: policy.MaxOccurrencesPerFingerprintSeverity,
                message: string.Create(
                    CultureInfo.InvariantCulture,
                    $"Fingerprint {group.Fingerprint.Id} executed {group.Occurrences} times; the budget is {budget}."),
                ruleName: RuleNames.MaxOccurrencesPerFingerprint,
                fingerprint: group.Fingerprint,
                expected: budget,
                actual: group.Occurrences,
                evidence:
                [
                    string.Create(CultureInfo.InvariantCulture, $"Occurrences: {group.Occurrences} (budget: {budget})"),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Total database time: {group.TotalDuration.TotalMilliseconds:F1} ms"),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"First seen at command #{group.FirstSequence}, last at command #{group.LastSequence}"),
                    string.Create(CultureInfo.InvariantCulture, $"SQL: {group.Fingerprint.NormalizedSql}"),
                ],
                isIgnored: ignoreReason is not null,
                ignoreReason: ignoreReason));
        }
    }

    private static void EvaluateDuplicateGroupBudget(
        List<QueryFinding> findings,
        IReadOnlyList<QueryFingerprintGroup> groups,
        QueryGuardPolicy policy)
    {
        if (policy.MaxDuplicateGroups is not { } budget)
        {
            return;
        }

        var repeatedGroups = new List<QueryFingerprintGroup>();
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].Occurrences >= policy.RepeatedQueryThreshold)
            {
                repeatedGroups.Add(groups[i]);
            }
        }

        if (repeatedGroups.Count <= budget)
        {
            return;
        }

        var evidence = new List<string>(repeatedGroups.Count + 1)
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"Repeated fingerprint groups: {repeatedGroups.Count} (budget: {budget}, repetition threshold: {policy.RepeatedQueryThreshold})"),
        };

        for (var i = 0; i < repeatedGroups.Count; i++)
        {
            evidence.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{repeatedGroups[i].Fingerprint.Id}: {repeatedGroups[i].Occurrences} occurrences"));
        }

        findings.Add(new QueryFinding(
            kind: QueryFindingKind.DuplicateGroupBudget,
            severity: policy.MaxDuplicateGroupsSeverity,
            // One repeated group is usually a single bug. Several at once is a structural problem, and
            // the message says which of the two this is.
            message: string.Create(
                CultureInfo.InvariantCulture,
                $"{repeatedGroups.Count} distinct queries repeated at or above the threshold; the budget is {budget}."),
            ruleName: RuleNames.MaxDuplicateGroups,
            expected: budget,
            actual: repeatedGroups.Count,
            evidence: evidence));
    }

    private static void EvaluateTotalDurationBudget(
        List<QueryFinding> findings,
        CompletedQueryGuardSession session,
        QueryGuardPolicy policy)
    {
        if (policy.MaxTotalDuration is not { } budget)
        {
            return;
        }

        var actual = session.TotalDatabaseDuration;
        if (actual <= budget)
        {
            return;
        }

        findings.Add(new QueryFinding(
            kind: QueryFindingKind.TotalDurationBudget,
            severity: policy.MaxTotalDurationSeverity,
            message: string.Create(
                CultureInfo.InvariantCulture,
                $"{session.Name} spent {actual.TotalMilliseconds:F1} ms in the database; the budget is {budget.TotalMilliseconds:F1} ms."),
            ruleName: RuleNames.MaxTotalDuration,
            expected: (long)budget.TotalMilliseconds,
            actual: (long)actual.TotalMilliseconds,
            evidence:
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Measured: {actual.TotalMilliseconds:F1} ms across {session.Records.Count} commands (budget: {budget.TotalMilliseconds:F1} ms)"),

                // Said in the finding itself, because someone reading a duration failure in CI is
                // about to decide whether to trust it.
                "Database timing varies with machine load. A duration budget belongs in an environment whose timing you control, not on a shared runner.",
            ]));
    }

    private static void EvaluateSlowQueries(
        List<QueryFinding> findings,
        CompletedQueryGuardSession session,
        QueryGuardPolicy policy)
    {
        if (policy.SlowQueryThreshold is not { } threshold)
        {
            return;
        }

        // Reported per fingerprint rather than per command, so one slow query executed fifty times
        // produces one finding instead of fifty.
        var reported = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < session.Records.Count; i++)
        {
            var record = session.Records[i];
            if (record.Duration <= threshold || !reported.Add(record.Fingerprint.Id))
            {
                continue;
            }

            findings.Add(new QueryFinding(
                kind: QueryFindingKind.SlowQuery,
                severity: policy.SlowQuerySeverity,
                message: string.Create(
                    CultureInfo.InvariantCulture,
                    $"Fingerprint {record.Fingerprint.Id} took {record.Duration.TotalMilliseconds:F1} ms; the threshold is {threshold.TotalMilliseconds:F1} ms."),
                ruleName: RuleNames.SlowQuery,
                fingerprint: record.Fingerprint,
                expected: (long)threshold.TotalMilliseconds,
                actual: (long)record.Duration.TotalMilliseconds,
                evidence:
                [
                    string.Create(CultureInfo.InvariantCulture, $"Slowest observed occurrence: command #{record.Sequence}"),
                    string.Create(CultureInfo.InvariantCulture, $"SQL: {record.Fingerprint.NormalizedSql}"),
                ]));
        }
    }

    private static void EvaluateCommandFailures(
        List<QueryFinding> findings,
        CompletedQueryGuardSession session)
    {
        if (session.FailedCommandCount == 0)
        {
            return;
        }

        for (var i = 0; i < session.Records.Count; i++)
        {
            var record = session.Records[i];
            if (!record.IsFailed)
            {
                continue;
            }

            findings.Add(new QueryFinding(
                kind: QueryFindingKind.CommandFailure,
                // Informational, always. The application already threw, and its exception is the
                // real report. QueryGuard adds context beside it and never competes with it.
                severity: QueryGuardSeverity.Information,
                message: string.Create(
                    CultureInfo.InvariantCulture,
                    $"A database command failed after {record.Duration.TotalMilliseconds:F1} ms ({record.FailureType})."),
                ruleName: RuleNames.CommandFailure,
                fingerprint: record.Fingerprint,
                evidence:
                [
                    string.Create(CultureInfo.InvariantCulture, $"Command #{record.Sequence}, kind {record.Kind}"),
                    string.Create(CultureInfo.InvariantCulture, $"SQL: {record.Fingerprint.NormalizedSql}"),
                    "The original exception is unchanged and remains the authoritative report of this failure.",
                ]));
        }
    }
}
