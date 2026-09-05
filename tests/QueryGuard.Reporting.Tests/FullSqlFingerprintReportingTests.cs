using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace QueryGuard.Reporting.Tests;

public class FullSqlFingerprintReportingTests
{
    [Fact]
    public void Reports_keep_bounded_sql_while_distinguishing_long_queries()
    {
        const int limit = 64;
        var projection = "SELECT " + string.Join(", ", Enumerable.Range(0, 700).Select(index => $"\"Column{index}\""));
        var factory = new QueryFingerprintFactory(new QueryGuardRedactor(
            new QueryGuardCaptureOptions { MaxNormalizedSqlLength = limit }));
        var session = new QueryGuardSession("report", QueryGuardPolicy.Create("report"));
        foreach (var table in new[] { "Customers", "Suppliers" })
        {
            var sql = projection + $" FROM \"{table}\" WHERE \"Name\" = 'private_value'";
            session.Record(QueryCommandKind.Reader, factory.Create(sql, QueryCommandKind.Reader), TimeSpan.Zero);
        }

        var result = new QueryGuardAnalyzer().Analyze(session.Complete());
        var json = new QueryGuardJsonReporter().Render(result);
        using var document = JsonDocument.Parse(json);
        var groups = document.RootElement.GetProperty("queryGroups").EnumerateArray().ToArray();

        Assert.Equal(2, groups.Length);
        Assert.NotEqual(groups[0].GetProperty("fingerprint").GetString(), groups[1].GetProperty("fingerprint").GetString());
        foreach (var group in groups)
        {
            var retainedSql = group.GetProperty("sql").GetString()!;
            Assert.Equal(limit + QueryGuardRedactor.TruncationMarker.Length, retainedSql.Length);
            Assert.EndsWith(QueryGuardRedactor.TruncationMarker, retainedSql, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("private_value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Column699", json, StringComparison.Ordinal);
        Assert.All(result.Records, record => Assert.Equal(limit + QueryGuardRedactor.TruncationMarker.Length,
            record.Fingerprint.NormalizedSql.Length));
    }
}
