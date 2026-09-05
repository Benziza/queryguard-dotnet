using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueryGuard.Testing;
using Xunit;

namespace QueryGuard.EntityFrameworkCore.Tests;

public class LongQueryFingerprintTests
{
    private static readonly string[] TableNames = ["Customers", "Suppliers"];

    [Fact]
    public async Task Wide_queries_on_different_tables_do_not_trigger_a_false_repetition_failure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DbContext>().UseSqlite(connection).UseQueryGuard().Options;
        await using var db = new DbContext(options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Customers (Id INTEGER); INSERT INTO Customers VALUES (1)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Suppliers (Id INTEGER); INSERT INTO Suppliers VALUES (1)");
        var projection = "SELECT Id AS Value, "
            + string.Join(", ", Enumerable.Range(0, 700).Select(index => $"Id AS \"Column{index}\""));
        await using var scope = QueryGuardScope.Start("wide reads",
            QueryGuardPolicy.Create("wide").WithMaxQueries(2).WithMaxOccurrencesPerFingerprint(1));

        var statements = TableNames
            .Select(table => projection + $" FROM \"{table}\"");
        foreach (var sql in statements)
        {
            Assert.Equal(1, Assert.Single(await db.Database.SqlQueryRaw<int>(sql).ToListAsync()));
        }

        var result = await scope.CompleteAsync();

        QueryGuardAssert.Passes(result);
        Assert.Equal(2, result.ReadCommandCount);
        Assert.Equal(2, result.Groups.Count);
        Assert.Equal(result.Groups[0].Fingerprint.NormalizedSql, result.Groups[1].Fingerprint.NormalizedSql);
        Assert.NotEqual(result.Groups[0].Fingerprint.Id, result.Groups[1].Fingerprint.Id);
    }
}
