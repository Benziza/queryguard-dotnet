using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueryGuard.Testing;
using Xunit;

namespace QueryGuard.EntityFrameworkCore.Tests;

/// <summary>
/// What a command <em>does</em>, when the provider puts something in front of the statement.
/// </summary>
/// <remarks>
/// <para>
/// These exist because of a bug the SQL Server suite found. EF Core's insert batch on SQL Server
/// begins with directives rather than with the interesting statement:
/// </para>
/// <code>
/// SET IMPLICIT_TRANSACTIONS OFF;
/// SET NOCOUNT ON;
/// INSERT INTO [Departments] ([Id], [CompanyId], [Name]) VALUES (@p0, @p1, @p2);
/// </code>
/// <para>
/// Classification tested only the leading keyword, saw <c>SET</c>, and left the command counted as a
/// read — so every <c>SaveChanges</c> on SQL Server consumed a read budget. The captured fixtures
/// could not notice, because a fixture only contains SQL somebody thought to capture.
/// </para>
/// <para>
/// A live SQL Server container is the only thing that finds this class of defect, and it is also too
/// slow and too Docker-dependent to be the only guard. So the shapes live here as well, driven
/// through the real interceptor against SQLite, where they run on every pull request in under a
/// second.
/// </para>
/// </remarks>
public class CommandClassificationTests
{
    [Theory]
    // The SQL Server insert batch, verbatim in shape.
    [InlineData("SET IMPLICIT_TRANSACTIONS OFF;\nSET NOCOUNT ON;\nINSERT INTO [T] ([A]) VALUES (@p0);")]
    [InlineData("SET NOCOUNT ON;\nUPDATE [T] SET [A] = @p0 WHERE [Id] = @p1;\nSELECT @@ROWCOUNT;")]
    [InlineData("SET NOCOUNT ON;\nDELETE FROM [T] WHERE [Id] = @p0;")]
    // SQLite's generated-key form, which was the original reason classification exists.
    [InlineData("INSERT INTO \"T\" (\"A\") VALUES (@p0) RETURNING \"Id\";")]
    // A leading declaration, which SQL Server emits when an insert needs an OUTPUT table.
    [InlineData("DECLARE @inserted0 TABLE ([Id] int);\nINSERT INTO [T] ([A]) OUTPUT INSERTED.[Id] INTO @inserted0 VALUES (@p0);")]
    public async Task A_write_behind_a_directive_prologue_is_not_a_read(string commandText)
    {
        var record = await ExecuteAsync(commandText, expectSuccess: false);

        Assert.False(record.IsRead);
    }

    [Theory]
    [InlineData("SELECT [A] FROM [T] WHERE [Id] = @p0;")]
    [InlineData("SET NOCOUNT ON;\nSELECT [A] FROM [T];")]
    public async Task A_read_stays_a_read(string commandText)
    {
        var record = await ExecuteAsync(commandText, expectSuccess: false);

        Assert.True(record.IsRead);
    }

    /// <summary>
    /// Pushes raw SQL through the real interceptor and returns the single record it captured.
    /// </summary>
    /// <remarks>
    /// The statements reference tables that do not exist, so every one of them fails. That is fine and
    /// deliberate: a failed command is still recorded, and classification is the thing under test —
    /// not whether SQLite can run SQL Server's syntax. It also exercises the failure path, where EF
    /// Core reports no execution method and the statement is the only evidence available.
    /// </remarks>
    private static async Task<QueryRecord> ExecuteAsync(string commandText, bool expectSuccess)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite(connection)
            .UseQueryGuard()
            .Options;

        await using var scope = QueryGuardScope.Start("classification", QueryGuardPolicy.Create("p"));

        using (var db = new SampleDbContext(options))
        {
            try
            {
                _ = await db.Database.ExecuteSqlRawAsync(commandText);
            }
            catch (SqliteException) when (!expectSuccess)
            {
                // Expected: the statement is here for its shape, not to run.
            }
        }

        var completed = await scope.CompleteAsync();

        return Assert.Single(completed.Records);
    }
}
