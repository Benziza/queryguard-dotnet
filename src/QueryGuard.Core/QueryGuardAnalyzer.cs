using System;
using System.Collections.Generic;
using System.Globalization;

namespace QueryGuard;

/// <summary>
/// Turns a completed session into a <see cref="QueryGuardResult"/>: groups commands by fingerprint
/// and reports what the evidence supports.
/// </summary>
/// <remarks>
/// <para>
/// Everything here runs <em>after</em> the request or test finished, never on the command path. That
/// separation is deliberate: capture has to be cheap because it happens per query, while analysis can
/// afford to sort and allocate because it happens once.
/// </para>
/// <para>
/// The analyzer is stateless and safe to share.
/// </para>
/// </remarks>
public sealed class QueryGuardAnalyzer
{
    private readonly IQueryGuardRedactor _redactor;
    private readonly IQueryBudgetEvaluator _budgetEvaluator;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardAnalyzer"/> class.
    /// </summary>
    /// <param name="redactor">
    /// Supplies the retention limits — how many samples a group keeps. Defaults to a redactor with
    /// default capture options.
    /// </param>
    /// <param name="budgetEvaluator">
    /// Decides whether what the session did is acceptable. Defaults to
    /// <see cref="QueryBudgetEvaluator"/>.
    /// </param>
    public QueryGuardAnalyzer(
        IQueryGuardRedactor? redactor = null,
        IQueryBudgetEvaluator? budgetEvaluator = null)
    {
        _redactor = redactor ?? new QueryGuardRedactor();
        _budgetEvaluator = budgetEvaluator ?? new QueryBudgetEvaluator();
    }

    /// <summary>
    /// Analyzes a completed session.
    /// </summary>
    /// <param name="session">The completed session.</param>
    /// <returns>The result, with groups and findings ordered deterministically.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public QueryGuardResult Analyze(CompletedQueryGuardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var policy = session.Policy;
        var groups = GroupByFingerprint(session, policy);
        var findings = new List<QueryFinding>();

        AddRepeatedQueryFindings(findings, groups, policy, session.Name);
        findings.AddRange(_budgetEvaluator.Evaluate(session, groups));

        // Findings arrive from two independent sources, so ordering is applied once, here, over the
        // whole set. Sorting each source separately would leave the combined list in an order that
        // depends on which rules happened to fire.
        findings.Sort(CompareFindings);

        return new QueryGuardResult(
            sessionName: session.Name,
            sessionId: session.Id,
            policyName: policy.Name,
            startedAt: session.StartedAt,
            elapsed: session.Elapsed,
            records: session.Records,
            groups: groups,
            findings: findings);
    }

    /// <summary>
    /// Groups a session's records by fingerprint, ordered so the most repeated query comes first.
    /// </summary>
    private IReadOnlyList<QueryFingerprintGroup> GroupByFingerprint(
        CompletedQueryGuardSession session,
        QueryGuardPolicy policy)
    {
        if (session.Records.Count == 0)
        {
            return Array.Empty<QueryFingerprintGroup>();
        }

        var accumulators = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

        // Insertion order is preserved separately so that ties in the final ordering break by first
        // appearance rather than by dictionary iteration order, which is not something to rely on.
        var order = new List<string>();

        foreach (var record in session.Records)
        {
            if (!policy.Counts(record.Kind))
            {
                // Writes are not grouped for repeated-query analysis. Saving fifty entities is one
                // operation with fifty statements, not a repeated-query problem.
                continue;
            }

            var id = record.Fingerprint.Id;
            if (!accumulators.TryGetValue(id, out var accumulator))
            {
                accumulator = new Accumulator(record);
                accumulators.Add(id, accumulator);
                order.Add(id);
            }
            else
            {
                accumulator.Add(record);
            }
        }

        var groups = new List<QueryFingerprintGroup>(accumulators.Count);
        foreach (var id in order)
        {
            groups.Add(accumulators[id].ToGroup(_redactor));
        }

        groups.Sort(CompareGroups);
        return groups;
    }

    private static void AddRepeatedQueryFindings(
        List<QueryFinding> findings,
        IReadOnlyList<QueryFingerprintGroup> groups,
        QueryGuardPolicy policy,
        string sessionName)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.Occurrences < policy.RepeatedQueryThreshold)
            {
                continue;
            }

            var isIgnored = QueryGuardQueryTag.HasIgnoreDirective(group.Tags);

            findings.Add(new QueryFinding(
                kind: QueryFindingKind.RepeatedQueryCandidate,
                // A warning, never a failure. Repeated SQL is evidence; turning evidence into a
                // build failure by default would break the first build QueryGuard is installed in.
                severity: QueryGuardSeverity.Warning,
                message: string.Create(
                    CultureInfo.InvariantCulture,
                    $"Potential N+1 pattern in {sessionName}: fingerprint {group.Fingerprint.Id} executed {group.Occurrences} times."),
                ruleName: RuleNames.RepeatedQuery,
                fingerprint: group.Fingerprint,
                expected: policy.RepeatedQueryThreshold,
                actual: group.Occurrences,
                evidence: BuildRepeatedQueryEvidence(group, policy),
                isIgnored: isIgnored,
                ignoreReason: isIgnored ? QueryGuardQueryTag.GetIgnoreReason(group.Tags) : null));
        }
    }

    private static IReadOnlyList<string> BuildRepeatedQueryEvidence(
        QueryFingerprintGroup group,
        QueryGuardPolicy policy)
        =>
        [
            string.Create(
                CultureInfo.InvariantCulture,
                $"Occurrences: {group.Occurrences} (warning threshold: {policy.RepeatedQueryThreshold})"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Total database time: {group.TotalDuration.TotalMilliseconds:F1} ms (average {group.AverageDuration.TotalMilliseconds:F2} ms)"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"First seen at command #{group.FirstSequence}, last at command #{group.LastSequence}"),
            string.Create(CultureInfo.InvariantCulture, $"SQL: {group.Fingerprint.NormalizedSql}"),

            // The limitation travels with the finding rather than living only in the documentation.
            // A reader who sees this message in a CI log usually has nothing else in front of them.
            "Repeated SQL is strong evidence, not proof of an application-level N+1 defect.",
            "Review eager loading, projection, or batching — or record an allowlist entry with a reason if the repetition is intentional.",
        ];

    /// <summary>
    /// Orders groups by descending occurrence count, then by descending total duration, then by
    /// fingerprint identifier.
    /// </summary>
    /// <remarks>
    /// The most repeated query first, because that is what a failure message should lead with. The
    /// tie-breakers exist so the ordering is total: two runs over the same data must produce
    /// byte-identical reports, or snapshot tests are worthless.
    /// </remarks>
    private static int CompareGroups(QueryFingerprintGroup left, QueryFingerprintGroup right)
    {
        var byOccurrences = right.Occurrences.CompareTo(left.Occurrences);
        if (byOccurrences != 0)
        {
            return byOccurrences;
        }

        var byDuration = right.TotalDuration.CompareTo(left.TotalDuration);
        return byDuration != 0
            ? byDuration
            : string.CompareOrdinal(left.Fingerprint.Id, right.Fingerprint.Id);
    }

    /// <summary>
    /// Orders findings by descending severity, then by rule name, then by fingerprint.
    /// </summary>
    /// <remarks>
    /// Failures before warnings, because a reader scanning CI output reads the top. Ignored findings
    /// sort after their reported equivalents so they never crowd out something actionable.
    /// </remarks>
    private static int CompareFindings(QueryFinding left, QueryFinding right)
    {
        var byIgnored = left.IsIgnored.CompareTo(right.IsIgnored);
        if (byIgnored != 0)
        {
            return byIgnored;
        }

        var bySeverity = right.Severity.CompareTo(left.Severity);
        if (bySeverity != 0)
        {
            return bySeverity;
        }

        var byRule = string.CompareOrdinal(left.RuleName, right.RuleName);
        if (byRule != 0)
        {
            return byRule;
        }

        return string.CompareOrdinal(
            left.Fingerprint?.Id ?? string.Empty,
            right.Fingerprint?.Id ?? string.Empty);
    }

    /// <summary>
    /// Accumulates one fingerprint's records while grouping.
    /// </summary>
    /// <remarks>
    /// A mutable accumulator behind an immutable result: the group is built once and then frozen, so
    /// nothing downstream can change what a report says.
    /// </remarks>
    private sealed class Accumulator
    {
        /// <summary>
        /// An upper bound on records held while grouping, before the redactor's own sample limit is
        /// applied. A fingerprint executed ten thousand times must not cause ten thousand records to
        /// be retained, and the redactor only ever keeps the earliest few.
        /// </summary>
        private const int MaxRetainedSamples = 16;

        private readonly List<QueryRecord> _samples = [];
        private readonly List<string> _tags = [];

        internal Accumulator(QueryRecord first)
        {
            Fingerprint = first.Fingerprint;
            Kind = first.Kind;
            FirstSequence = first.Sequence;
            Add(first);
        }

        private QueryFingerprint Fingerprint { get; }

        private QueryCommandKind Kind { get; }

        private int FirstSequence { get; }

        private int LastSequence { get; set; }

        private int Occurrences { get; set; }

        private int FailureCount { get; set; }

        private TimeSpan TotalDuration { get; set; }

        internal void Add(QueryRecord record)
        {
            Occurrences++;
            LastSequence = record.Sequence;
            TotalDuration += record.Duration;

            if (record.IsFailed)
            {
                FailureCount++;
            }

            if (_samples.Count < MaxRetainedSamples)
            {
                _samples.Add(record);
            }

            for (var i = 0; i < record.Tags.Count; i++)
            {
                var tag = record.Tags[i];
                if (!_tags.Contains(tag))
                {
                    _tags.Add(tag);
                }
            }
        }

        internal QueryFingerprintGroup ToGroup(IQueryGuardRedactor redactor)
            => new(
                fingerprint: Fingerprint,
                occurrences: Occurrences,
                totalDuration: TotalDuration,
                firstSequence: FirstSequence,
                lastSequence: LastSequence,
                kind: Kind,
                failureCount: FailureCount,
                samples: redactor.LimitSamples<QueryRecord>(_samples),
                tags: _tags);
    }
}
