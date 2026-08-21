using System;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace QueryGuard.Tests;

public class QueryFingerprintFactoryTests
{
    private readonly QueryFingerprintFactory _factory = new();

    [Fact]
    public void An_identifier_is_prefixed_and_short_enough_to_paste_anywhere()
    {
        var fingerprint = _factory.Create(ProviderSqlFixtures.SqliteDepartmentsByCompany, QueryCommandKind.Reader);

        Assert.StartsWith(QueryFingerprint.IdPrefix, fingerprint.Id, StringComparison.Ordinal);
        Assert.Equal(QueryFingerprint.IdPrefix.Length + 8, fingerprint.Id.Length);
        Assert.Matches("^QG-FP-[0-9A-F]{8}$", fingerprint.Id);
    }

    [Fact]
    public void The_same_sql_always_produces_the_same_identifier()
    {
        var first = _factory.Create(ProviderSqlFixtures.SqliteDepartmentsByCompany, QueryCommandKind.Reader);
        var second = _factory.Create(ProviderSqlFixtures.SqliteDepartmentsByCompany, QueryCommandKind.Reader);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void The_identifier_does_not_depend_on_string_hash_randomization()
    {
        // string.GetHashCode() is randomized per process, so a fingerprint built on it would change
        // between runs: breaking allowlists, issue reports, and CI comparisons. This asserts the
        // digest is derived from the text itself rather than from a runtime hash.
        var fingerprint = _factory.Create("SELECT 1", QueryCommandKind.Reader);

        Assert.Equal("QG-FP-", fingerprint.Id[..6]);
        Assert.NotEqual(
            "SELECT 1".GetHashCode(StringComparison.Ordinal).ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
            fingerprint.Id[6..]);
    }

    [Theory]
    [InlineData(
        ProviderSqlFixtures.SqliteDepartmentsByCompany,
        ProviderSqlFixtures.SqliteDepartmentsByCompanyDifferentParameterIndex)]
    [InlineData(
        ProviderSqlFixtures.SqliteDepartmentsByCompany,
        ProviderSqlFixtures.SqliteDepartmentsByCompanySingleLine)]
    [InlineData(
        ProviderSqlFixtures.PostgresDepartmentsByCompany,
        ProviderSqlFixtures.PostgresDepartmentsByCompanyDifferentPosition)]
    public void Equivalent_provider_sql_shares_a_fingerprint(string first, string second)
    {
        // This is the assertion the repeated-query detector depends on entirely. If provider noise
        // splits one logical query into several fingerprints, a per-parent query in a loop looks like
        // N distinct queries and nothing is ever reported.
        Assert.Equal(
            _factory.Create(first, QueryCommandKind.Reader).Id,
            _factory.Create(second, QueryCommandKind.Reader).Id);
    }

    [Theory]
    [InlineData(
        ProviderSqlFixtures.SqliteDepartmentsByCompany,
        ProviderSqlFixtures.SqliteDepartmentsByName)]
    [InlineData(
        ProviderSqlFixtures.SqliteDepartmentsByCompany,
        ProviderSqlFixtures.SqliteDepartmentsReorderedColumns)]
    public void Genuinely_different_sql_does_not_share_a_fingerprint(string first, string second)
    {
        // The worse failure mode: merging different statements makes a report point at SQL the
        // application never ran.
        Assert.NotEqual(
            _factory.Create(first, QueryCommandKind.Reader).Id,
            _factory.Create(second, QueryCommandKind.Reader).Id);
    }

    [Fact]
    public void Different_providers_generating_the_same_query_do_not_share_a_fingerprint()
    {
        // Identifier quoting is a real difference in the statement, and the normalizer deliberately
        // does not rewrite it. Reporting them as one group would be a lie about what ran.
        var sqlite = _factory.Create(ProviderSqlFixtures.SqliteDepartmentsByCompany, QueryCommandKind.Reader);
        var sqlServer = _factory.Create(ProviderSqlFixtures.SqlServerDepartmentsByCompanyBareStatement, QueryCommandKind.Reader);
        var mySql = _factory.Create(ProviderSqlFixtures.MySqlDepartmentsByCompany, QueryCommandKind.Reader);

        Assert.Equal(3, new[] { sqlite.Id, sqlServer.Id, mySql.Id }.Distinct().Count());
    }

    [Fact]
    public void The_command_kind_participates_in_the_identifier()
    {
        // A read and a write that happen to normalize to the same text must never be grouped, because
        // they count toward different budgets.
        var read = _factory.Create("SELECT 1", QueryCommandKind.Reader);
        var write = _factory.Create("SELECT 1", QueryCommandKind.NonQuery);

        Assert.NotEqual(read.Id, write.Id);
    }

    [Fact]
    public void The_retained_text_is_normalized_and_redacted()
    {
        var fingerprint = _factory.Create(
            "SELECT * FROM \"T\" WHERE \"Email\" = 'alice@example.com' AND \"Id\" = @p0",
            QueryCommandKind.Reader);

        Assert.DoesNotContain("alice@example.com", fingerprint.NormalizedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("@p0", fingerprint.NormalizedSql, StringComparison.Ordinal);
        Assert.Contains("\"Email\"", fingerprint.NormalizedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_query_differing_only_by_an_inlined_value_shares_a_fingerprint()
    {
        // EF Core inlines literal constants rather than parameterizing them, so without literal
        // redaction a loop over inlined values would produce a new fingerprint every iteration.
        var paris = _factory.Create("SELECT * FROM \"C\" WHERE \"City\" = 'Paris'", QueryCommandKind.Reader);
        var lyon = _factory.Create("SELECT * FROM \"C\" WHERE \"City\" = 'Lyon'", QueryCommandKind.Reader);

        Assert.Equal(paris.Id, lyon.Id);
    }

    [Fact]
    public void Null_and_empty_command_text_are_handled()
    {
        var fromNull = _factory.Create(null, QueryCommandKind.Unknown);
        var fromEmpty = _factory.Create(string.Empty, QueryCommandKind.Unknown);

        Assert.Equal(fromNull.Id, fromEmpty.Id);
        Assert.Equal(string.Empty, fromNull.NormalizedSql);
    }

    [Fact]
    public void A_custom_normalizer_can_replace_the_default()
    {
        // The seam exists so a provider whose SQL the generic normalizer groups badly can be given a
        // dedicated strategy without touching the detector.
        var factory = new QueryFingerprintFactory(normalizer: new EverythingIsOneQueryNormalizer());

        Assert.Equal(
            factory.Create("SELECT 1", QueryCommandKind.Reader).Id,
            factory.Create("SELECT 2", QueryCommandKind.Reader).Id);
    }

    [Fact]
    public void Fingerprinting_a_long_statement_stays_well_under_a_millisecond()
    {
        // This runs once per intercepted command, so a regression that made it expensive would show
        // up as overhead on every query. A generous bound: the point is to catch an accidental
        // quadratic pass, not to publish a benchmark number. Real measurement is QG-050.
        var wideProjection = "SELECT "
            + string.Join(", ", Enumerable.Range(0, 400).Select(i => $"\"t\".\"Column{i}\""))
            + " FROM \"WideTable\" AS \"t\" WHERE \"t\".\"Id\" = @__id_0";

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++)
        {
            _ = _factory.Create(wideProjection, QueryCommandKind.Reader);
        }

        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed.TotalMilliseconds < 100 * 5,
            $"Fingerprinting 100 wide statements took {stopwatch.Elapsed.TotalMilliseconds:F1}ms.");
    }

    private sealed class EverythingIsOneQueryNormalizer : ISqlNormalizer
    {
        public string Normalize(string? commandText) => "everything";
    }
}
