using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace QueryGuard;

/// <summary>
/// The default <see cref="IQueryFingerprintFactory"/>: redact, then hash.
/// </summary>
/// <remarks>
/// <para>
/// The identifier appears in allowlist configuration, in issue reports, and in CI output, so it has
/// to be identical across runs, processes, and both target frameworks.
/// <see cref="string.GetHashCode()"/> is randomized per process and is therefore disqualified;
/// this uses an explicit SHA-256 digest truncated to eight hex characters.
/// </para>
/// <para>
/// Eight characters is 32 bits, which is short enough to paste into a configuration file or an
/// issue title. Collisions are irrelevant at the scale that matters: a fingerprint only has to be
/// unique among the handful of distinct statements inside one request or test, not globally.
/// </para>
/// <para>
/// Text is redacted <em>before</em> hashing, never after, so two commands differing only by an
/// inlined value share an identifier — and no un-redacted text is ever retained.
/// </para>
/// </remarks>
public sealed class QueryFingerprintFactory : IQueryFingerprintFactory
{
    /// <summary>
    /// How many hex characters of the digest form the identifier.
    /// </summary>
    private const int IdLength = 8;

    private readonly IQueryGuardRedactor _redactor;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryFingerprintFactory"/> class.
    /// </summary>
    /// <param name="redactor">
    /// The redactor applied before hashing. Defaults to a redactor with default capture options.
    /// </param>
    public QueryFingerprintFactory(IQueryGuardRedactor? redactor = null)
        => _redactor = redactor ?? new QueryGuardRedactor();

    /// <inheritdoc />
    public QueryFingerprint Create(string? commandText, QueryCommandKind kind)
    {
        var normalized = _redactor.RedactSql(commandText);
        return new QueryFingerprint(ComputeId(normalized, kind), normalized);
    }

    private static string ComputeId(string normalizedSql, QueryCommandKind kind)
    {
        // The command kind participates in the identifier so that a read and a write which happen
        // to normalize to the same text are never grouped together. It is written as its numeric
        // value followed by a colon, which keeps the payload unambiguous without embedding a
        // control character in the source.
        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)kind}:{normalizedSql}");

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));

        var builder = new StringBuilder(
            QueryFingerprint.IdPrefix,
            QueryFingerprint.IdPrefix.Length + IdLength);

        for (var i = 0; i < IdLength / 2; i++)
        {
            builder.Append(digest[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
