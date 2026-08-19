namespace QueryGuard;

/// <summary>
/// The identifiers of the rules QueryGuard evaluates.
/// </summary>
/// <remarks>
/// These names reach users: they appear in every finding, in JSON reports, and as JUnit test-case
/// names, so a CI dashboard or a saved query can be built on them. That makes them part of the public
/// contract — renaming one is a breaking change to the report schema, not a refactor. See
/// <c>docs/decisions/0011-versioning.md</c>.
/// </remarks>
public static class RuleNames
{
    /// <summary>
    /// The same normalized SQL was executed repeatedly inside one scope.
    /// </summary>
    public const string RepeatedQuery = "repeated-query";

    /// <summary>
    /// The session executed more counted commands than its budget allows.
    /// </summary>
    public const string MaxQueries = "max-queries";

    /// <summary>
    /// A single fingerprint occurred more times than its budget allows.
    /// </summary>
    public const string MaxOccurrencesPerFingerprint = "max-occurrences-per-fingerprint";

    /// <summary>
    /// More fingerprints reached the repetition threshold than the budget allows.
    /// </summary>
    public const string MaxDuplicateGroups = "max-duplicate-groups";

    /// <summary>
    /// The summed duration of captured commands exceeded its budget.
    /// </summary>
    public const string MaxTotalDuration = "max-total-duration";

    /// <summary>
    /// A single command took longer than the configured threshold.
    /// </summary>
    public const string SlowQuery = "slow-query";

    /// <summary>
    /// A database command failed.
    /// </summary>
    public const string CommandFailure = "command-failure";
}
