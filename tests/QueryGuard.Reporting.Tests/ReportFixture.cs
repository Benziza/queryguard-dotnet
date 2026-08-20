using System;
using System.Collections.Generic;
using System.Globalization;

namespace QueryGuard.Reporting.Tests;

/// <summary>
/// Builds results with fixed values, so a rendered report can be compared byte for byte.
/// </summary>
/// <remarks>
/// Every duration and identifier here is fixed. A snapshot test over output that contains a real
/// measurement can only ever assert on fragments; fixing the inputs is what makes asserting on the
/// whole document possible.
/// </remarks>
internal static class ReportFixture
{
    internal static readonly DateTimeOffset FixedInstant = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    internal static readonly Guid FixedSessionId = new("0f9a1c2d-3e4b-5a6c-7d8e-9f0a1b2c3d4e");

    /// <summary>
    /// A repeated-query candidate that also breaches a per-fingerprint budget: the shape a real
    /// failing report has.
    /// </summary>
    internal static QueryGuardResult FailingResult()
    {
        var busy = Fingerprint("1A2B3C4D", "SELECT \"d\".\"Id\", \"d\".\"Name\" FROM \"Departments\" AS \"d\" WHERE \"d\".\"CompanyId\" = ?");
        var quiet = Fingerprint("9E8D7C6B", "SELECT \"c\".\"Id\", \"c\".\"Name\" FROM \"Companies\" AS \"c\"");

        return Result(
            groups:
            [
                Group(busy, occurrences: 51, totalMs: 84.3, firstSequence: 2, lastSequence: 52),
                Group(quiet, occurrences: 1, totalMs: 1.2, firstSequence: 1, lastSequence: 1),
            ],
            findings:
            [
                new QueryFinding(
                    QueryFindingKind.FingerprintOccurrenceBudget,
                    QueryGuardSeverity.Failure,
                    "Fingerprint QG-FP-1A2B3C4D executed 51 times; the budget is 5.",
                    RuleNames.MaxOccurrencesPerFingerprint,
                    busy,
                    expected: 5,
                    actual: 51,
                    evidence: ["Occurrences: 51 (budget: 5)", "Total database time: 84.3 ms"]),
                new QueryFinding(
                    QueryFindingKind.RepeatedQueryCandidate,
                    QueryGuardSeverity.Warning,
                    "Potential N+1 pattern in GET /api/companies: fingerprint QG-FP-1A2B3C4D executed 51 times.",
                    RuleNames.RepeatedQuery,
                    busy,
                    expected: 3,
                    actual: 51,
                    evidence:
                    [
                        "Occurrences: 51 (warning threshold: 3)",
                        "Repeated SQL is strong evidence, not proof of an application-level N+1 defect.",
                    ]),
            ],
            readCommands: 52);
    }

    /// <summary>
    /// A result whose only finding was allowlisted, for asserting that ignored findings are emitted
    /// rather than dropped.
    /// </summary>
    internal static QueryGuardResult IgnoredResult()
    {
        var busy = Fingerprint("1A2B3C4D", "SELECT \"d\".\"Id\" FROM \"Departments\" AS \"d\" WHERE \"d\".\"CompanyId\" = ?");

        return Result(
            groups: [Group(busy, occurrences: 9, totalMs: 12.5, firstSequence: 1, lastSequence: 9)],
            findings:
            [
                new QueryFinding(
                    QueryFindingKind.RepeatedQueryCandidate,
                    QueryGuardSeverity.Warning,
                    "Potential N+1 pattern in GET /api/companies: fingerprint QG-FP-1A2B3C4D executed 9 times.",
                    RuleNames.RepeatedQuery,
                    busy,
                    expected: 3,
                    actual: 9,
                    evidence: ["Occurrences: 9 (warning threshold: 3)"],
                    isIgnored: true,
                    ignoreReason: "Bounded provider lookup; at most three report sections."),
            ],
            readCommands: 9);
    }

    /// <summary>
    /// A clean result: no findings at all.
    /// </summary>
    internal static QueryGuardResult CleanResult()
    {
        var quiet = Fingerprint("9E8D7C6B", "SELECT \"c\".\"Id\" FROM \"Companies\" AS \"c\"");

        return Result(
            groups: [Group(quiet, occurrences: 1, totalMs: 1.2, firstSequence: 1, lastSequence: 1)],
            findings: [],
            readCommands: 1);
    }

    internal static QueryGuardResult ManyFindingsResult(int count)
    {
        var groups = new List<QueryFingerprintGroup>(count);
        var findings = new List<QueryFinding>(count);

        for (var i = 0; i < count; i++)
        {
            var fingerprint = Fingerprint(
                i.ToString("X8", CultureInfo.InvariantCulture),
                string.Create(CultureInfo.InvariantCulture, $"SELECT * FROM \"T{i}\" WHERE \"Id\" = ?"));

            groups.Add(Group(fingerprint, occurrences: 4, totalMs: 4, firstSequence: 1, lastSequence: 4));
            findings.Add(new QueryFinding(
                QueryFindingKind.RepeatedQueryCandidate,
                QueryGuardSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"Potential N+1 pattern: fingerprint {fingerprint.Id} executed 4 times."),
                RuleNames.RepeatedQuery,
                fingerprint,
                expected: 3,
                actual: 4));
        }

        return Result(groups, findings, readCommands: count * 4);
    }

    private static QueryGuardResult Result(
        List<QueryFingerprintGroup> groups,
        List<QueryFinding> findings,
        int readCommands)
    {
        var records = new List<QueryRecord>(readCommands);
        for (var i = 0; i < readCommands; i++)
        {
            records.Add(new QueryRecord(
                sequence: i + 1,
                kind: QueryCommandKind.Reader,
                fingerprint: groups.Count > 0 ? groups[0].Fingerprint : Fingerprint("00000000", "SELECT 1"),
                duration: TimeSpan.FromMilliseconds(1),
                startedAt: FixedInstant));
        }

        return new QueryGuardResult(
            sessionName: "GET /api/companies",
            sessionId: FixedSessionId,
            policyName: "companies",
            startedAt: FixedInstant,
            elapsed: TimeSpan.FromMilliseconds(120),
            records: records,
            groups: groups,
            findings: findings);
    }

    private static QueryFingerprint Fingerprint(string suffix, string normalizedSql)
        => new(QueryFingerprint.IdPrefix + suffix, normalizedSql);

    private static QueryFingerprintGroup Group(
        QueryFingerprint fingerprint,
        int occurrences,
        double totalMs,
        int firstSequence,
        int lastSequence)
        => new(
            fingerprint,
            occurrences,
            TimeSpan.FromMilliseconds(totalMs),
            firstSequence,
            lastSequence,
            QueryCommandKind.Reader);
}
