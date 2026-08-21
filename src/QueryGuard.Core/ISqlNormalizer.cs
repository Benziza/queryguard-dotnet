namespace QueryGuard;

/// <summary>
/// Reduces provider-generated SQL to a comparable form so that two executions of the same logical
/// query produce the same text.
/// </summary>
/// <remarks>
/// The two failure modes are not symmetric, and that asymmetry drives every design choice here.
/// Over-normalizing merges genuinely different statements, which makes a report point at the wrong
/// SQL: actively misleading. Under-normalizing splits one logical query into several groups, so a
/// real repeated-query pattern goes unreported: the tool is merely quieter. When in doubt, do less.
/// See <c>docs/decisions/0005-sql-fingerprints.md</c>.
/// </remarks>
public interface ISqlNormalizer
{
    /// <summary>
    /// Normalizes a command's text.
    /// </summary>
    /// <param name="commandText">The SQL as the provider produced it. May be <see langword="null"/>.</param>
    /// <returns>
    /// Normalized SQL with the same token order, or an empty string when there was nothing to
    /// normalize.
    /// </returns>
    string Normalize(string? commandText);
}
