namespace QueryGuard;

/// <summary>
/// Turns a database command's text into a stable <see cref="QueryFingerprint"/>.
/// </summary>
/// <remarks>
/// This is the seam that decides when two commands count as "the same query", which is the
/// judgement the whole repeated-query feature rests on. It is an interface so that a provider whose
/// SQL the generic implementation groups badly can be given a dedicated strategy without touching
/// the detector. See <c>docs/decisions/0005-sql-fingerprints.md</c>.
/// </remarks>
public interface IQueryFingerprintFactory
{
    /// <summary>
    /// Creates a fingerprint for a command.
    /// </summary>
    /// <param name="commandText">The command text as the provider produced it.</param>
    /// <param name="kind">The kind of command.</param>
    /// <returns>A fingerprint whose identifier is stable across runs, processes, and frameworks.</returns>
    QueryFingerprint Create(string? commandText, QueryCommandKind kind);
}
