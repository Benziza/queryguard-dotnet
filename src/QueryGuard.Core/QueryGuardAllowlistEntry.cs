using System;

namespace QueryGuard;

/// <summary>
/// Records that a specific repeated query is intentional, and why.
/// </summary>
/// <remarks>
/// <para>
/// The reason is not optional, and that is the entire design. "Turn this off" is not something a
/// reviewer can evaluate; "bounded provider lookup, at most three report sections" is. An allowlist
/// entry is a claim about the code that someone can check, and it appears in a pull request diff
/// where they can.
/// </para>
/// <para>
/// A matched entry marks a finding as ignored. It never removes it — see
/// <c>docs/decisions/0003-detector-terminology.md</c>. If we tell users some findings will be wrong,
/// we owe them a way to say so that does not also make the tool blind.
/// </para>
/// </remarks>
public sealed class QueryGuardAllowlistEntry
{
    private QueryGuardAllowlistEntry(string? fingerprintId, string? queryTag, string reason)
    {
        FingerprintId = fingerprintId;
        QueryTag = queryTag;
        Reason = reason;
    }

    /// <summary>
    /// Gets the fingerprint identifier this entry matches, or <see langword="null"/> when it matches
    /// by query tag instead.
    /// </summary>
    public string? FingerprintId { get; }

    /// <summary>
    /// Gets the query tag this entry matches, or <see langword="null"/> when it matches by
    /// fingerprint instead.
    /// </summary>
    public string? QueryTag { get; }

    /// <summary>
    /// Gets why the repetition is intentional.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Allows a specific fingerprint.
    /// </summary>
    /// <param name="fingerprintId">The fingerprint identifier, for example <c>QG-FP-1A2B3C4D</c>.</param>
    /// <param name="reason">Why the repetition is intentional.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentException">The identifier or the reason is empty or whitespace.</exception>
    /// <remarks>
    /// Matching by fingerprint is precise and brittle in a useful way: if the query changes, its
    /// fingerprint changes and the entry stops matching, so the exception has to be justified again.
    /// That is better than an entry that silently keeps suppressing a query nobody recognizes.
    /// </remarks>
    public static QueryGuardAllowlistEntry ForFingerprint(string fingerprintId, string reason)
    {
        if (string.IsNullOrWhiteSpace(fingerprintId))
        {
            throw new ArgumentException("A fingerprint identifier is required.", nameof(fingerprintId));
        }

        return new QueryGuardAllowlistEntry(fingerprintId, queryTag: null, RequireReason(reason));
    }

    /// <summary>
    /// Allows any query carrying a given tag.
    /// </summary>
    /// <param name="queryTag">
    /// The tag applied with EF Core's <c>TagWith</c>, for example <c>bounded-reference-lookup</c>.
    /// </param>
    /// <param name="reason">Why the repetition is intentional.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentException">The tag or the reason is empty or whitespace.</exception>
    /// <remarks>
    /// Matching by tag survives a query changing, which makes it the right choice for a pattern that
    /// is intentional by design rather than by accident — and it keeps the declaration next to the
    /// LINQ that needs it.
    /// </remarks>
    public static QueryGuardAllowlistEntry ForQueryTag(string queryTag, string reason)
    {
        if (string.IsNullOrWhiteSpace(queryTag))
        {
            throw new ArgumentException("A query tag is required.", nameof(queryTag));
        }

        return new QueryGuardAllowlistEntry(fingerprintId: null, queryTag, RequireReason(reason));
    }

    /// <summary>
    /// Determines whether this entry matches a finding's fingerprint and tags.
    /// </summary>
    /// <param name="fingerprintId">The fingerprint identifier, if the finding has one.</param>
    /// <param name="tags">The tags recognized on the fingerprint's group.</param>
    /// <returns><see langword="true"/> when this entry applies.</returns>
    public bool Matches(string? fingerprintId, System.Collections.Generic.IReadOnlyList<string>? tags)
    {
        if (FingerprintId is not null)
        {
            return fingerprintId is not null
                && string.Equals(FingerprintId, fingerprintId, StringComparison.Ordinal);
        }

        if (QueryTag is null || tags is null)
        {
            return false;
        }

        for (var i = 0; i < tags.Count; i++)
        {
            // A tag arrives as the whole directive text, so a substring match is what lets
            // `QueryGuard:Ignore reason=bounded-lookup` be allowlisted as `bounded-lookup`.
            if (tags[i].Contains(QueryTag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public override string ToString()
        => FingerprintId is not null
            ? $"fingerprint {FingerprintId}: {Reason}"
            : $"tag {QueryTag}: {Reason}";

    private static string RequireReason(string reason)
        => string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException(
                "An allowlist entry requires a reason. An exception a reviewer cannot evaluate is not an exception, it is a blind spot.",
                nameof(reason))
            : reason;
}
