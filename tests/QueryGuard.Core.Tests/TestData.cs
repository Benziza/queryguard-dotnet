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
        => new(name, policy ?? QueryGuardPolicy.Create("test"), () => FixedInstant);
}
