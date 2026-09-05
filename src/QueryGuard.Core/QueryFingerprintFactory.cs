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
/// inlined value share an identifier, and no un-redacted text is ever retained.
/// </para>
/// </remarks>
public sealed class QueryFingerprintFactory : IQueryFingerprintFactory
{
    /// <summary>
    /// How many hex characters of the digest form the identifier.
    /// </summary>
    private const int IdLength = 8;

    private readonly IQueryGuardRedactor _redactor;
    private readonly ISqlNormalizer _normalizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryFingerprintFactory"/> class.
    /// </summary>
    /// <param name="redactor">
    /// The redactor applied after normalization. Defaults to a redactor with default capture options.
    /// With <see cref="QueryGuardRedactor"/>, the full redacted SQL is hashed before display truncation.
    /// Custom redactors retain their existing contract: their <see cref="IQueryGuardRedactor.RedactSql"/>
    /// output is both hashed and retained.
    /// </param>
    /// <param name="normalizer">
    /// The normalizer applied first. Defaults to <see cref="SqlNormalizer"/>.
    /// </param>
    public QueryFingerprintFactory(IQueryGuardRedactor? redactor = null, ISqlNormalizer? normalizer = null)
    {
        _redactor = redactor ?? new QueryGuardRedactor();
        _normalizer = normalizer ?? new SqlNormalizer();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Normalize first, then redact. Normalization removes the provider noise that would otherwise
    /// split one logical query into several groups; redaction then removes the values, so no
    /// un-redacted text is ever hashed or retained. The built-in redactor truncates only after hashing,
    /// so the display limit cannot merge queries whose differences occur later in the statement.
    /// </remarks>
    public QueryFingerprint Create(string? commandText, QueryCommandKind kind)
    {
        var normalized = _normalizer.Normalize(commandText);
        if (_redactor is QueryGuardRedactor builtInRedactor)
        {
            var redacted = builtInRedactor.RedactSqlForFingerprint(normalized);
            var id = ComputeId(redacted, kind);
            return new QueryFingerprint(id, builtInRedactor.TruncateSql(redacted));
        }

        // A custom policy may remove more than literals. Never bypass it or change shared options
        // to recover text it chose not to return. Custom factories can supply other hashing policies.
        var customRedacted = _redactor.RedactSql(normalized);
        return new QueryFingerprint(ComputeId(customRedacted, kind), customRedacted);
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
