namespace QueryGuard;

/// <summary>
/// How strongly QueryGuard reacts when a rule is not satisfied.
/// </summary>
/// <remarks>
/// Severity is configured per rule rather than per policy, so a single policy can warn about a
/// repeated-query candidate while failing outright on a per-fingerprint budget breach.
/// </remarks>
public enum QueryGuardSeverity
{
    /// <summary>
    /// The rule is evaluated and reported, but never changes the outcome. Used for
    /// informational evidence such as a recorded command failure.
    /// </summary>
    Information = 0,

    /// <summary>
    /// The rule was not satisfied and the result is reported, but
    /// <see cref="QueryGuardResult.IsSuccess"/> stays <see langword="true"/>.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// The rule was not satisfied and the result is a failure. In a test this is what
    /// <c>QueryGuardAssert</c> turns into a thrown exception; in an HTTP request it only
    /// affects reporting, because the middleware never alters application behavior.
    /// </summary>
    Failure = 2,
}
