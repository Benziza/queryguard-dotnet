using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueryGuard.Testing;
using Xunit;

namespace QueryGuard.EntityFrameworkCore.Tests;

public class ReadClassificationRegressionTests
{
    [Theory]
    [InlineData("SELECT ';DELETE' AS \"Value\"")]
    [InlineData("SELECT 'text'';UPDATE' AS \"Value\"")]
    [InlineData("SELECT 'ok' /* ;DELETE FROM T */ AS \"Value\"")]
    [InlineData("SELECT 'ok' -- ;TRUNCATE T\n AS \"Value\"")]
    [InlineData("SELECT \"a;DROP\" AS \"Value\" FROM (SELECT 'ok' AS \"a;DROP\")")]
    [InlineData("SELECT [a;ALTER] AS \"Value\" FROM (SELECT 'ok' AS [a;ALTER])")]
    [InlineData("SELECT `a;INSERT` AS \"Value\" FROM (SELECT 'ok' AS `a;INSERT`)")]
    [InlineData("SELECT \"a\"\";DELETE\" AS \"Value\" FROM (SELECT 'ok' AS \"a\"\";DELETE\")")]
    [InlineData("SELECT `a``;DELETE` AS \"Value\" FROM (SELECT 'ok' AS `a``;DELETE`)")]
    public async Task Reads_with_sql_syntax_inside_values_identifiers_or_comments_consume_the_budget(string sql)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DbContext>().UseSqlite(connection).UseQueryGuard().Options;
        await using var db = new DbContext(options);

        foreach (var asynchronous in new[] { false, true })
        {
            await using var scope = QueryGuardScope.Start("read", QueryGuardPolicy.Create("read").WithMaxQueries(0));
            var query = db.Database.SqlQueryRaw<string>(sql);
            var values = asynchronous ? await query.ToListAsync() : query.ToList();
            Assert.Single(values);

            var result = await scope.CompleteAsync();
            Assert.Equal(QueryCommandKind.Reader, Assert.Single(result.Records).Kind);
            Assert.Equal(1, result.ReadCommandCount);
            Assert.Throws<QueryGuardBudgetExceededException>(() => QueryGuardAssert.Passes(result));
        }
    }

    [Theory]
    [InlineData("SELECT ';DELETE' FROM \"Missing\"")]
    [InlineData("SELECT [a;DROP] FROM \"Missing\"")]
    [InlineData("SELECT * /* ;UPDATE T */ FROM \"Missing\"")]
    public async Task Failed_reads_keep_the_original_exception_and_read_count(string sql)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DbContext>().UseSqlite(connection).UseQueryGuard().Options;
        await using var db = new DbContext(options);
        await using var scope = QueryGuardScope.Start("failed read", QueryGuardPolicy.Create("read"));

        await Assert.ThrowsAsync<SqliteException>(() => db.Database.SqlQueryRaw<string>(sql).ToListAsync());

        var result = await scope.CompleteAsync();
        var record = Assert.Single(result.Records);
        Assert.True(record.IsFailed);
        Assert.Equal(QueryCommandKind.Reader, record.Kind);
        Assert.Equal(1, result.ReadCommandCount);
    }

    [Theory]
    [InlineData("-- QueryGuard:Ignore reason=seed\nINSERT INTO Writes DEFAULT VALUES RETURNING Id AS Value")]
    [InlineData("/* write with ;SELECT inside a comment */ INSERT INTO Writes DEFAULT VALUES RETURNING Id AS Value")]
    public async Task Writes_with_leading_comments_and_returning_rows_do_not_consume_read_budgets(string sql)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DbContext>().UseSqlite(connection).UseQueryGuard().Options;
        await using var db = new DbContext(options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Writes (Id INTEGER PRIMARY KEY)");

        foreach (var asynchronous in new[] { false, true })
        {
            await using var scope = QueryGuardScope.Start("write", QueryGuardPolicy.Create("write").WithMaxQueries(0));
            var query = db.Database.SqlQueryRaw<int>(sql);
            var values = asynchronous ? await query.ToListAsync() : query.ToList();
            Assert.Single(values);

            var result = await scope.CompleteAsync();
            Assert.Equal(QueryCommandKind.NonQuery, Assert.Single(result.Records).Kind);
            Assert.Equal(0, result.ReadCommandCount);
            QueryGuardAssert.Passes(result);
        }
    }
}
