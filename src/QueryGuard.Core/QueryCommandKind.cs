namespace QueryGuard;

/// <summary>
/// The kind of relational command that produced a <see cref="QueryRecord"/>.
/// </summary>
/// <remarks>
/// Budgets are configured per kind. A budget of ten read queries that silently counted
/// <c>SaveChanges</c> writes as well would mean something different on every endpoint, so the
/// kind is captured and the detector only analyses the kinds a policy opts into.
/// </remarks>
public enum QueryCommandKind
{
    /// <summary>
    /// The kind could not be determined. Treated as excluded from every budget.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A command executed for its result set, such as the SQL behind
    /// <c>ToList</c> or <c>FirstOrDefault</c>. This is the kind repeated-query detection targets.
    /// </summary>
    Reader = 1,

    /// <summary>
    /// A command executed for a single scalar result, such as the SQL behind <c>Count</c>
    /// or <c>Any</c>.
    /// </summary>
    Scalar = 2,

    /// <summary>
    /// A command executed for its affected-row count, such as the SQL behind
    /// <c>SaveChanges</c> or <c>ExecuteUpdate</c>.
    /// </summary>
    NonQuery = 3,
}
