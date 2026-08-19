using System;

namespace QueryGuard;

/// <summary>
/// A stable identity for a normalized SQL statement, together with the normalized text that
/// justifies it.
/// </summary>
/// <remarks>
/// <para>
/// The identifier is what appears in allowlist configuration, in issue reports, and in CI
/// output, so it must be identical across processes, runs, and target frameworks. The
/// normalized text travels with it because a fingerprint a developer cannot read is not
/// evidence.
/// </para>
/// <para>
/// Fingerprints group <em>textually equivalent</em> SQL. They are deliberately conservative:
/// grouping two genuinely different statements would make a report actively misleading, while
/// failing to group two equivalent ones only makes QueryGuard quieter. See
/// <c>docs/decisions/0005-sql-fingerprints.md</c>.
/// </para>
/// </remarks>
public sealed class QueryFingerprint : IEquatable<QueryFingerprint>
{
    /// <summary>
    /// The prefix on every fingerprint identifier, making it recognizable in logs and issue text.
    /// </summary>
    public const string IdPrefix = "QG-FP-";

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryFingerprint"/> class.
    /// </summary>
    /// <param name="id">
    /// The stable identifier, conventionally <c>QG-FP-</c> followed by an uppercase hex digest.
    /// </param>
    /// <param name="normalizedSql">The normalized, redacted SQL the identifier was derived from.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="normalizedSql"/> is <see langword="null"/>.</exception>
    public QueryFingerprint(string id, string normalizedSql)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A fingerprint identifier is required.", nameof(id));
        }

        Id = id;
        NormalizedSql = normalizedSql ?? throw new ArgumentNullException(nameof(normalizedSql));
    }

    /// <summary>
    /// Gets the stable identifier, for example <c>QG-FP-1A2B3C4D</c>.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the normalized and redacted SQL this fingerprint was derived from.
    /// </summary>
    /// <remarks>
    /// Whitespace is collapsed, non-semantic comments are removed, parameter references are
    /// replaced by placeholders, and surviving literals are redacted. Tokens are never reordered.
    /// </remarks>
    public string NormalizedSql { get; }

    /// <inheritdoc />
    public bool Equals(QueryFingerprint? other)
        => other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as QueryFingerprint);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);

    /// <inheritdoc />
    public override string ToString() => Id;
}
