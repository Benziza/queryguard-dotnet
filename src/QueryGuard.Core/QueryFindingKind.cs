namespace QueryGuard;

/// <summary>
/// What kind of evidence a <see cref="QueryFinding"/> reports.
/// </summary>
public enum QueryFindingKind
{
    /// <summary>
    /// The same normalized SQL was executed repeatedly inside one scope.
    /// </summary>
    /// <remarks>
    /// This is evidence of a <em>potential</em> N+1 pattern, not proof of one. QueryGuard observes
    /// SQL text, so it can prove repetition and nothing more. Bounded lookups, retries, and
    /// deliberate per-tenant fan-out all produce repeated SQL legitimately. See
    /// <c>docs/decisions/0003-detector-terminology.md</c>.
    /// </remarks>
    RepeatedQueryCandidate = 0,

    /// <summary>
    /// The session executed more read commands than its budget allows.
    /// </summary>
    TotalQueryBudget = 1,

    /// <summary>
    /// A single fingerprint occurred more times than its budget allows.
    /// </summary>
    /// <remarks>
    /// This is the rule that actually catches an N+1 regression: a total-count budget can stay
    /// satisfied while one query quietly repeats.
    /// </remarks>
    FingerprintOccurrenceBudget = 2,

    /// <summary>
    /// More fingerprints exceeded the repetition threshold than the budget allows.
    /// </summary>
    /// <remarks>
    /// One repeated group is usually a single bug. Five repeated groups in one endpoint is a
    /// structural problem, and a policy should be able to say so.
    /// </remarks>
    DuplicateGroupBudget = 3,

    /// <summary>
    /// The summed duration of captured commands exceeded the budget.
    /// </summary>
    /// <remarks>
    /// Disabled by default. CI machines are noisy, and a duration budget that fires
    /// intermittently teaches users to distrust every other finding.
    /// </remarks>
    TotalDurationBudget = 4,

    /// <summary>
    /// A single command took longer than the configured threshold.
    /// </summary>
    SlowQuery = 5,

    /// <summary>
    /// A database command failed. Recorded as evidence alongside the original exception, which is
    /// never replaced or hidden.
    /// </summary>
    CommandFailure = 6,
}
