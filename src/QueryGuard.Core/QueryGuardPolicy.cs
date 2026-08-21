using System;
using System.Collections.Generic;
using System.Globalization;

namespace QueryGuard;

/// <summary>
/// A named set of query budgets and thresholds evaluated against a completed session.
/// </summary>
/// <remarks>
/// <para>
/// A policy is immutable. Each <c>With…</c> method returns a new instance, so a policy can be
/// safely shared as a singleton, captured in a field, and reused across concurrent requests.
/// </para>
/// <para>
/// Severity is per rule rather than per policy: warning on a total-count budget while failing on
/// a per-fingerprint budget is the most useful default combination, because the second rule is
/// the one that actually catches an N+1 regression.
/// </para>
/// <para>
/// Every limit is opt-in and starts unset, with one exception: the repeated-query threshold
/// defaults to <see cref="DefaultRepeatedQueryThreshold"/> so QueryGuard has something useful to
/// say without configuration. It produces a warning, never a failure.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var policy = QueryGuardPolicy.Create("companies")
///     .WithMaxQueries(20, QueryGuardSeverity.Warning)
///     .WithMaxOccurrencesPerFingerprint(5, QueryGuardSeverity.Failure);
/// </code>
/// </example>
public sealed class QueryGuardPolicy
{
    /// <summary>
    /// The number of occurrences of one fingerprint at which QueryGuard warns by default.
    /// </summary>
    /// <remarks>
    /// Three, not two. Two occurrences of the same query inside one request is common and usually
    /// benign; three is the point at which a loop becomes the more likely explanation. The
    /// threshold is the single most important false-positive control QueryGuard has, so it errs
    /// toward silence.
    /// </remarks>
    public const int DefaultRepeatedQueryThreshold = 3;

    private static readonly QueryCommandKind[] DefaultCountedKinds =
    [
        QueryCommandKind.Reader,
        QueryCommandKind.Scalar,
    ];

    private static readonly QueryGuardAllowlistEntry[] NoAllowlistEntries = [];

    private QueryGuardPolicy(string name)
    {
        Name = name;
        RepeatedQueryThreshold = DefaultRepeatedQueryThreshold;
        CountedKinds = DefaultCountedKinds;
        Allowlist = NoAllowlistEntries;
    }

    private QueryGuardPolicy(QueryGuardPolicy source)
    {
        Name = source.Name;
        RepeatedQueryThreshold = source.RepeatedQueryThreshold;
        CountedKinds = source.CountedKinds;
        MaxQueries = source.MaxQueries;
        MaxQueriesSeverity = source.MaxQueriesSeverity;
        MaxOccurrencesPerFingerprint = source.MaxOccurrencesPerFingerprint;
        MaxOccurrencesPerFingerprintSeverity = source.MaxOccurrencesPerFingerprintSeverity;
        MaxDuplicateGroups = source.MaxDuplicateGroups;
        MaxDuplicateGroupsSeverity = source.MaxDuplicateGroupsSeverity;
        MaxTotalDuration = source.MaxTotalDuration;
        MaxTotalDurationSeverity = source.MaxTotalDurationSeverity;
        SlowQueryThreshold = source.SlowQueryThreshold;
        SlowQuerySeverity = source.SlowQuerySeverity;
        Allowlist = source.Allowlist;
    }

    /// <summary>
    /// Gets the policy name. It appears in every finding and report, so it should identify the
    /// route or test it guards.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the number of occurrences of a single fingerprint that produces a repeated-query
    /// candidate warning.
    /// </summary>
    public int RepeatedQueryThreshold { get; private set; }

    /// <summary>
    /// Gets the command kinds counted toward budgets. Defaults to reader and scalar commands.
    /// </summary>
    /// <remarks>
    /// Writes are excluded by default so that a budget of ten means ten reads regardless of how
    /// many entities the endpoint happens to save.
    /// </remarks>
    public IReadOnlyList<QueryCommandKind> CountedKinds { get; private set; }

    /// <summary>
    /// Gets the maximum number of counted commands allowed in the session, or
    /// <see langword="null"/> when unlimited.
    /// </summary>
    public int? MaxQueries { get; private set; }

    /// <summary>
    /// Gets the severity applied when <see cref="MaxQueries"/> is exceeded.
    /// </summary>
    public QueryGuardSeverity MaxQueriesSeverity { get; private set; } = QueryGuardSeverity.Failure;

    /// <summary>
    /// Gets the maximum number of occurrences allowed for any single fingerprint, or
    /// <see langword="null"/> when unlimited.
    /// </summary>
    public int? MaxOccurrencesPerFingerprint { get; private set; }

    /// <summary>
    /// Gets the severity applied when <see cref="MaxOccurrencesPerFingerprint"/> is exceeded.
    /// </summary>
    public QueryGuardSeverity MaxOccurrencesPerFingerprintSeverity { get; private set; } = QueryGuardSeverity.Failure;

    /// <summary>
    /// Gets the maximum number of fingerprints allowed to reach
    /// <see cref="RepeatedQueryThreshold"/>, or <see langword="null"/> when unlimited.
    /// </summary>
    public int? MaxDuplicateGroups { get; private set; }

    /// <summary>
    /// Gets the severity applied when <see cref="MaxDuplicateGroups"/> is exceeded.
    /// </summary>
    public QueryGuardSeverity MaxDuplicateGroupsSeverity { get; private set; } = QueryGuardSeverity.Failure;

    /// <summary>
    /// Gets the maximum summed command duration allowed, or <see langword="null"/> when unlimited.
    /// </summary>
    /// <remarks>
    /// Unset by default, and it should stay unset outside an environment you control. Shared CI
    /// machines are noisy enough that a duration budget produces intermittent failures, and an
    /// intermittently failing guard teaches users to distrust every finding it reports.
    /// </remarks>
    public TimeSpan? MaxTotalDuration { get; private set; }

    /// <summary>
    /// Gets the severity applied when <see cref="MaxTotalDuration"/> is exceeded.
    /// </summary>
    public QueryGuardSeverity MaxTotalDurationSeverity { get; private set; } = QueryGuardSeverity.Warning;

    /// <summary>
    /// Gets the duration above which a single command is reported as slow, or
    /// <see langword="null"/> when disabled.
    /// </summary>
    public TimeSpan? SlowQueryThreshold { get; private set; }

    /// <summary>
    /// Gets the severity applied when a command exceeds <see cref="SlowQueryThreshold"/>.
    /// </summary>
    public QueryGuardSeverity SlowQuerySeverity { get; private set; } = QueryGuardSeverity.Warning;

    /// <summary>
    /// Gets the intentional repetitions this policy has recorded, each with its reason.
    /// </summary>
    /// <remarks>
    /// An allowlisted finding is reported as ignored rather than removed, so this list narrows what
    /// fails without narrowing what is visible.
    /// </remarks>
    public IReadOnlyList<QueryGuardAllowlistEntry> Allowlist { get; private set; }

    /// <summary>
    /// Creates a policy with default thresholds and no budgets.
    /// </summary>
    /// <param name="name">
    /// A name identifying what this policy guards, such as a route pattern or a test name.
    /// </param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public static QueryGuardPolicy Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A policy name is required; it identifies the policy in every report.", nameof(name));
        }

        return new QueryGuardPolicy(name);
    }

    /// <summary>
    /// Returns a copy of this policy under a different name, keeping every configured limit.
    /// </summary>
    /// <param name="name">The new policy name.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public QueryGuardPolicy WithName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A policy name is required.", nameof(name));
        }

        var copy = new QueryGuardPolicy(name);
        return CopyLimitsTo(copy);
    }

    /// <summary>
    /// Limits how many counted commands the session may execute.
    /// </summary>
    /// <param name="maxQueries">The inclusive maximum. Exactly this many commands passes.</param>
    /// <param name="severity">How to react when the limit is exceeded.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxQueries"/> is negative.</exception>
    public QueryGuardPolicy WithMaxQueries(int maxQueries, QueryGuardSeverity severity = QueryGuardSeverity.Failure)
    {
        ThrowIfNegative(maxQueries, nameof(maxQueries));

        return new QueryGuardPolicy(this)
        {
            MaxQueries = maxQueries,
            MaxQueriesSeverity = severity,
        };
    }

    /// <summary>
    /// Sets how many occurrences of one fingerprint produce a repeated-query candidate warning.
    /// </summary>
    /// <param name="threshold">The occurrence count at which to warn. Must be at least two.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="threshold"/> is less than two.</exception>
    public QueryGuardPolicy WithRepeatedQueryThreshold(int threshold)
    {
        if (threshold < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                threshold,
                "A repeated-query threshold below two would report every single query as repeated.");
        }

        return new QueryGuardPolicy(this) { RepeatedQueryThreshold = threshold };
    }

    /// <summary>
    /// Limits how many times any single fingerprint may occur.
    /// </summary>
    /// <param name="maxOccurrences">The inclusive maximum per fingerprint.</param>
    /// <param name="severity">How to react when the limit is exceeded.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxOccurrences"/> is less than one.</exception>
    public QueryGuardPolicy WithMaxOccurrencesPerFingerprint(
        int maxOccurrences,
        QueryGuardSeverity severity = QueryGuardSeverity.Failure)
    {
        if (maxOccurrences < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxOccurrences),
                maxOccurrences,
                "A per-fingerprint budget of zero would fail on the first query of any kind.");
        }

        return new QueryGuardPolicy(this)
        {
            MaxOccurrencesPerFingerprint = maxOccurrences,
            MaxOccurrencesPerFingerprintSeverity = severity,
        };
    }

    /// <summary>
    /// Limits how many fingerprints may reach <see cref="RepeatedQueryThreshold"/>.
    /// </summary>
    /// <param name="maxGroups">The inclusive maximum number of repeated groups.</param>
    /// <param name="severity">How to react when the limit is exceeded.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxGroups"/> is negative.</exception>
    public QueryGuardPolicy WithMaxDuplicateGroups(
        int maxGroups,
        QueryGuardSeverity severity = QueryGuardSeverity.Failure)
    {
        ThrowIfNegative(maxGroups, nameof(maxGroups));

        return new QueryGuardPolicy(this)
        {
            MaxDuplicateGroups = maxGroups,
            MaxDuplicateGroupsSeverity = severity,
        };
    }

    /// <summary>
    /// Limits the summed duration of counted commands.
    /// </summary>
    /// <param name="maxTotalDuration">The inclusive maximum total duration.</param>
    /// <param name="severity">How to react when the limit is exceeded.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxTotalDuration"/> is negative.</exception>
    /// <remarks>
    /// Use this only in an environment whose timing you control. See <see cref="MaxTotalDuration"/>.
    /// </remarks>
    public QueryGuardPolicy WithMaxTotalDuration(
        TimeSpan maxTotalDuration,
        QueryGuardSeverity severity = QueryGuardSeverity.Warning)
    {
        if (maxTotalDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTotalDuration), maxTotalDuration, "A duration budget cannot be negative.");
        }

        return new QueryGuardPolicy(this)
        {
            MaxTotalDuration = maxTotalDuration,
            MaxTotalDurationSeverity = severity,
        };
    }

    /// <summary>
    /// Reports any single command slower than the given threshold.
    /// </summary>
    /// <param name="threshold">The duration above which a command is reported as slow.</param>
    /// <param name="severity">How to react.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="threshold"/> is negative.</exception>
    public QueryGuardPolicy WithSlowQueryThreshold(
        TimeSpan threshold,
        QueryGuardSeverity severity = QueryGuardSeverity.Warning)
    {
        if (threshold < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold), threshold, "A slow-query threshold cannot be negative.");
        }

        return new QueryGuardPolicy(this)
        {
            SlowQueryThreshold = threshold,
            SlowQuerySeverity = severity,
        };
    }

    /// <summary>
    /// Sets which command kinds count toward budgets.
    /// </summary>
    /// <param name="kinds">The kinds to count. Must contain at least one kind.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentException"><paramref name="kinds"/> is null or empty.</exception>
    public QueryGuardPolicy WithCountedKinds(params QueryCommandKind[] kinds)
    {
        if (kinds is null || kinds.Length == 0)
        {
            throw new ArgumentException(
                "A policy that counts no command kinds can never report anything.", nameof(kinds));
        }

        return new QueryGuardPolicy(this) { CountedKinds = (QueryCommandKind[])kinds.Clone() };
    }

    /// <summary>
    /// Records that a specific fingerprint's repetition is intentional.
    /// </summary>
    /// <param name="fingerprintId">The fingerprint identifier, for example <c>QG-FP-1A2B3C4D</c>.</param>
    /// <param name="reason">Why the repetition is intentional.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentException">The identifier or the reason is empty or whitespace.</exception>
    /// <example>
    /// <code>
    /// policy = policy.AllowFingerprint(
    ///     "QG-FP-1A2B3C4D",
    ///     reason: "Bounded provider lookup; at most three report sections.");
    /// </code>
    /// </example>
    public QueryGuardPolicy AllowFingerprint(string fingerprintId, string reason)
        => Allow(QueryGuardAllowlistEntry.ForFingerprint(fingerprintId, reason));

    /// <summary>
    /// Records that any query carrying a given tag repeats intentionally.
    /// </summary>
    /// <param name="queryTag">The tag applied with EF Core's <c>TagWith</c>.</param>
    /// <param name="reason">Why the repetition is intentional.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentException">The tag or the reason is empty or whitespace.</exception>
    public QueryGuardPolicy AllowQueryTag(string queryTag, string reason)
        => Allow(QueryGuardAllowlistEntry.ForQueryTag(queryTag, reason));

    /// <summary>
    /// Adds an allowlist entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>A new policy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    public QueryGuardPolicy Allow(QueryGuardAllowlistEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var entries = new QueryGuardAllowlistEntry[Allowlist.Count + 1];
        for (var i = 0; i < Allowlist.Count; i++)
        {
            entries[i] = Allowlist[i];
        }

        entries[^1] = entry;

        return new QueryGuardPolicy(this) { Allowlist = entries };
    }

    /// <summary>
    /// Finds the reason a finding is allowlisted, if it is.
    /// </summary>
    /// <param name="fingerprintId">The finding's fingerprint identifier, if it has one.</param>
    /// <param name="tags">The tags recognized on the finding's query.</param>
    /// <returns>The reason, or <see langword="null"/> when no entry matches.</returns>
    /// <remarks>
    /// A <c>QueryGuard:Ignore</c> directive on the query itself is handled separately, by the
    /// detector. Both routes produce the same outcome: a finding marked ignored, with its reason, so
    /// a user can declare an exception wherever it belongs: next to the query, or next to the
    /// policy that guards the endpoint.
    /// </remarks>
    public string? FindAllowlistReason(string? fingerprintId, IReadOnlyList<string>? tags)
    {
        for (var i = 0; i < Allowlist.Count; i++)
        {
            if (Allowlist[i].Matches(fingerprintId, tags))
            {
                return Allowlist[i].Reason;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether a command of the given kind counts toward this policy's budgets.
    /// </summary>
    /// <param name="kind">The command kind.</param>
    /// <returns><see langword="true"/> when the kind is counted.</returns>
    public bool Counts(QueryCommandKind kind)
    {
        // A small array beats a set here: CountedKinds has one or two entries in practice, and
        // this runs once per record during finalization.
        for (var i = 0; i < CountedKinds.Count; i++)
        {
            if (CountedKinds[i] == kind)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var maxQueries = MaxQueries?.ToString(CultureInfo.InvariantCulture) ?? "unlimited";
        var perFingerprint = MaxOccurrencesPerFingerprint?.ToString(CultureInfo.InvariantCulture) ?? "unlimited";
        return $"{Name} (max queries: {maxQueries}, per fingerprint: {perFingerprint}, repeat warning at: {RepeatedQueryThreshold})";
    }

    private static void ThrowIfNegative(int value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "A budget cannot be negative.");
        }
    }

    private QueryGuardPolicy CopyLimitsTo(QueryGuardPolicy target)
    {
        target.RepeatedQueryThreshold = RepeatedQueryThreshold;
        target.CountedKinds = CountedKinds;
        target.MaxQueries = MaxQueries;
        target.MaxQueriesSeverity = MaxQueriesSeverity;
        target.MaxOccurrencesPerFingerprint = MaxOccurrencesPerFingerprint;
        target.MaxOccurrencesPerFingerprintSeverity = MaxOccurrencesPerFingerprintSeverity;
        target.MaxDuplicateGroups = MaxDuplicateGroups;
        target.MaxDuplicateGroupsSeverity = MaxDuplicateGroupsSeverity;
        target.MaxTotalDuration = MaxTotalDuration;
        target.MaxTotalDurationSeverity = MaxTotalDurationSeverity;
        target.SlowQueryThreshold = SlowQueryThreshold;
        target.SlowQuerySeverity = SlowQuerySeverity;

        // The ASP.NET Core integration renames the default policy per route. Dropping the allowlist
        // here would resurrect every suppressed finding on every endpoint.
        target.Allowlist = Allowlist;

        return target;
    }
}
