using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace QueryGuard.Tests;

public class FullSqlFingerprintTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(QueryGuardCaptureOptions.DefaultMaxNormalizedSqlLength)]
    public void Differences_after_the_display_limit_keep_distinct_fingerprints(int limit)
    {
        var factory = Factory(limit);
        var projection = WideProjection();
        var customers = factory.Create(projection + " FROM \"Customers\"", QueryCommandKind.Reader);
        var suppliers = factory.Create(projection + " FROM \"Suppliers\"", QueryCommandKind.Reader);

        Assert.NotEqual(customers.Id, suppliers.Id);
        Assert.Equal(customers.NormalizedSql, suppliers.NormalizedSql);
        Assert.Equal(limit + QueryGuardRedactor.TruncationMarker.Length, customers.NormalizedSql.Length);
        Assert.EndsWith(QueryGuardRedactor.TruncationMarker, customers.NormalizedSql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(QueryGuardCaptureOptions.DefaultMaxNormalizedSqlLength)]
    public void Changing_the_display_limit_does_not_change_the_identifier(int limit)
    {
        var sql = WideProjection() + " FROM \"Customers\" WHERE \"Name\" = 'private_value'";
        var bounded = Factory(limit).Create(sql, QueryCommandKind.Reader);
        var complete = Factory(int.MaxValue).Create(sql, QueryCommandKind.Reader);

        Assert.Equal(complete.Id, bounded.Id);
        Assert.NotEqual(complete.NormalizedSql, bounded.NormalizedSql);
        Assert.DoesNotContain("private_value", complete.NormalizedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Values_and_parameter_names_beyond_the_limit_are_redacted_before_hashing()
    {
        var projection = WideProjection();
        var firstSql = projection + " FROM \"T\" WHERE \"Name\" = 'private_one' AND \"Id\" = 42 AND \"Other\" = @p0";
        var secondSql = projection + " FROM \"T\" WHERE \"Name\" = 'private_two' AND \"Id\" = 99 AND \"Other\" = @p8";
        var first = Factory(64).Create(firstSql, QueryCommandKind.Reader);
        var second = Factory(64).Create(secondSql, QueryCommandKind.Reader);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(Factory(int.MaxValue).Create(firstSql, QueryCommandKind.Reader).Id, first.Id);
    }

    [Fact]
    public void Redaction_opt_out_still_affects_the_full_hash_input()
    {
        var options = new QueryGuardCaptureOptions
        {
            MaxNormalizedSqlLength = 8,
            RedactStringLiterals = false,
            RedactNumericLiterals = false,
        };
        var factory = new QueryFingerprintFactory(new QueryGuardRedactor(options));

        Assert.NotEqual(
            factory.Create("SELECT * FROM T WHERE Name = 'first' AND Id = 42", QueryCommandKind.Reader).Id,
            factory.Create("SELECT * FROM T WHERE Name = 'second' AND Id = 99", QueryCommandKind.Reader).Id);
    }

    [Fact]
    public void The_command_kind_still_participates_in_a_long_query_identifier()
    {
        var factory = Factory(64);
        var sql = WideProjection() + " FROM \"T\"";

        Assert.NotEqual(factory.Create(sql, QueryCommandKind.Reader).Id,
            factory.Create(sql, QueryCommandKind.NonQuery).Id);
    }

    [Fact]
    public void Distinct_long_queries_do_not_fail_a_repetition_budget()
    {
        var factory = Factory(64);
        var projection = WideProjection();
        var first = factory.Create(projection + " FROM \"Customers\"", QueryCommandKind.Reader);
        var second = factory.Create(projection + " FROM \"Suppliers\"", QueryCommandKind.Reader);
        var session = new QueryGuardSession("wide queries", QueryGuardPolicy.Create("wide").WithMaxOccurrencesPerFingerprint(1));
        session.Record(QueryCommandKind.Reader, first, TimeSpan.Zero);
        session.Record(QueryCommandKind.Reader, second, TimeSpan.Zero);

        var result = new QueryGuardAnalyzer().Analyze(session.Complete());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Groups.Count);
        Assert.All(result.Groups, group => Assert.Equal(1, group.Occurrences));
    }

    [Fact]
    public void Real_repetitions_of_a_long_query_still_fail_the_budget()
    {
        var factory = Factory(64);
        var sql = WideProjection() + " FROM \"T\"";
        var session = new QueryGuardSession("repeated wide query", QueryGuardPolicy.Create("wide").WithMaxOccurrencesPerFingerprint(1));
        session.Record(QueryCommandKind.Reader, factory.Create(sql, QueryCommandKind.Reader), TimeSpan.Zero);
        session.Record(QueryCommandKind.Reader, factory.Create(sql, QueryCommandKind.Reader), TimeSpan.Zero);

        var result = new QueryGuardAnalyzer().Analyze(session.Complete());

        Assert.False(result.IsSuccess);
        Assert.Equal(2, Assert.Single(result.Groups).Occurrences);
    }

    [Fact]
    public void Fingerprinting_does_not_change_the_shared_redactors_retention_limit()
    {
        var redactor = new QueryGuardRedactor(new QueryGuardCaptureOptions { MaxNormalizedSqlLength = 16 });
        var sql = WideProjection() + " FROM \"T\"";
        var before = redactor.RedactSql(sql);

        _ = new QueryFingerprintFactory(redactor).Create(sql, QueryCommandKind.Reader);

        Assert.Equal(16, redactor.Options.MaxNormalizedSqlLength);
        Assert.Equal(before, redactor.RedactSql(sql));
    }

    [Fact]
    public void A_custom_redactors_output_remains_authoritative()
    {
        var redactor = new CustomRedactor();
        var fingerprint = new QueryFingerprintFactory(redactor).Create("SELECT 'private_value'", QueryCommandKind.Reader);
        var expected = new QueryFingerprintFactory().Create(CustomRedactor.SafeSql, QueryCommandKind.Reader);

        Assert.Equal(1, redactor.Calls);
        Assert.Equal(CustomRedactor.SafeSql, fingerprint.NormalizedSql);
        Assert.Equal(expected.Id, fingerprint.Id);
    }

    private static QueryFingerprintFactory Factory(int limit)
        => new(new QueryGuardRedactor(new QueryGuardCaptureOptions { MaxNormalizedSqlLength = limit }));

    private static string WideProjection()
        => "SELECT " + string.Join(", ", Enumerable.Range(0, 700).Select(index => $"\"Column{index}\""));

    private sealed class CustomRedactor : IQueryGuardRedactor
    {
        internal const string SafeSql = "SELECT masked";

        public QueryGuardCaptureOptions Options { get; } = new();

        internal int Calls { get; private set; }

        public string RedactSql(string? sql)
        {
            Calls++;
            return SafeSql;
        }

        public string? FilterStackTrace(string? stackTrace) => null;

        public IReadOnlyList<T> LimitSamples<T>(IReadOnlyList<T> samples) => samples;
    }
}
