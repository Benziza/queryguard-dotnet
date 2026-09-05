using System;
using Xunit;

namespace QueryGuard.Reporting.Tests;

public class ProviderStringPrivacyTests
{
    [Theory]
    [InlineData("SELECT $$private_value$$")]
    [InlineData("SELECT $tag$private_value @p0 -- comment\n /* text */$tag$")]
    [InlineData("SELECT E'prefix\\'private_value'")]
    [InlineData("SELECT 'prefix\\'private_value'")]
    public void Captured_literals_do_not_reach_any_sql_report(string sql)
    {
        var session = new QueryGuardSession("privacy", QueryGuardPolicy.Create("privacy").WithMaxQueries(0));
        var fingerprint = new QueryFingerprintFactory().Create(sql, QueryCommandKind.Reader);
        session.Record(QueryCommandKind.Reader, fingerprint, TimeSpan.Zero);
        var result = new QueryGuardAnalyzer().Analyze(session.Complete());
        Assert.False(result.IsSuccess);
        QueryGuardReporter[] reporters =
        [
            new QueryGuardJsonReporter(),
            new QueryGuardConsoleReporter(),
            new QueryGuardJUnitReporter(),
            new QueryGuardSarifReporter(),
        ];

        foreach (var reporter in reporters)
        {
            Assert.DoesNotContain("private_value", reporter.Render(result), StringComparison.Ordinal);
        }
    }
}
