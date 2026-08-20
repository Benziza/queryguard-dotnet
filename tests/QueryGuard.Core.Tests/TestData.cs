using System;
using System.Globalization;

namespace QueryGuard.Tests;

/// <summary>
/// Shared builders for the synthetic values used across the Core test suite.
/// </summary>
/// <remarks>
/// Everything here is synthetic. No SQL, schema name, or output in this repository comes from a
/// real application.
/// </remarks>
internal static class TestData
{
    /// <summary>
    /// A fixed instant, so that a test asserting on timestamps never depends on the clock.
    /// </summary>
    public static readonly DateTimeOffset FixedInstant =
        new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    public static QueryFingerprint Fingerprint(string suffix = "1A2B3C4D", string? normalizedSql = null)
        => new(
            QueryFingerprint.IdPrefix + suffix,
            normalizedSql ?? "SELECT \"d\".\"Id\", \"d\".\"Name\" FROM \"Departments\" AS \"d\" WHERE \"d\".\"CompanyId\" = @p");

    public static QueryFingerprint FingerprintFor(int index)
        => Fingerprint(index.ToString("X8", CultureInfo.InvariantCulture));

    public static QueryRecord Record(
        int sequence = 1,
        QueryCommandKind kind = QueryCommandKind.Reader,
        QueryFingerprint? fingerprint = null,
        double durationMs = 1.5,
        bool isFailed = false)
        => new(
            sequence: sequence,
            kind: kind,
            fingerprint: fingerprint ?? Fingerprint(),
            duration: TimeSpan.FromMilliseconds(durationMs),
            startedAt: FixedInstant,
            commandSource: "Linq",
            parameterCount: 1,
            isFailed: isFailed,
            failureType: isFailed ? "Microsoft.Data.Sqlite.SqliteException" : null);

    public static QueryGuardSession Session(
        string name = "GET /api/companies",
        QueryGuardPolicy? policy = null)
        => new(name, policy ?? QueryGuardPolicy.Create("test"), clock: () => FixedInstant);

    /// <summary>
    /// An analyzed result with the shape a test asks for.
    /// </summary>
    /// <param name="scope">The scope name.</param>
    /// <param name="reads">Total counted read commands.</param>
    /// <param name="groups">How many distinct fingerprints to spread them across.</param>
    /// <param name="topOccurrences">How many of the reads belong to the most repeated fingerprint.</param>
    /// <remarks>
    /// The remaining fingerprints get one command each, so <paramref name="reads"/> has to equal
    /// <paramref name="topOccurrences"/> plus <paramref name="groups"/> minus one. Asserted rather than
    /// silently adjusted: a test that asks for an impossible shape has a bug in the test, and quietly
    /// producing a different shape would make it pass for the wrong reason.
    /// </remarks>
    public static QueryGuardResult ResultWith(string scope, int reads, int groups, int topOccurrences)
    {
        if (groups < 1 || topOccurrences < 1 || reads != topOccurrences + groups - 1)
        {
            throw new ArgumentException(
                $"Cannot build a result with {reads} reads across {groups} groups where the top one ran "
                + $"{topOccurrences} times.",
                nameof(reads));
        }

        var session = Session(scope);

        for (var i = 0; i < topOccurrences; i++)
        {
            session.Record(QueryCommandKind.Reader, FingerprintFor(0), TimeSpan.FromMilliseconds(1));
        }

        for (var group = 1; group < groups; group++)
        {
            session.Record(QueryCommandKind.Reader, FingerprintFor(group), TimeSpan.FromMilliseconds(1));
        }

        return new QueryGuardAnalyzer().Analyze(session.Complete());
    }
}
