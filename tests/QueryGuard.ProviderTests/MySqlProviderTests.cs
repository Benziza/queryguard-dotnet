using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QueryGuard.EntityFrameworkCore;
using Testcontainers.MySql;
using Xunit;

namespace QueryGuard.ProviderTests;

/// <summary>
/// QueryGuard against a real MySQL instance.
/// </summary>
/// <remarks>
/// <para>
/// MySQL brings a third identifier-quoting style — backticks — and inlines some constants the other
/// providers parameterize, so it exercises the literal-redaction path where SQL Server exercises the
/// parameter path. Both have to end up hiding the value.
/// </para>
/// <para>
/// Through <c>MySql.EntityFrameworkCore</c>, Oracle's provider, and not Pomelo. Pomelo is the more
/// widely used of the two, but its latest release is <c>9.0.0</c> with no EF Core 10 line, and this
/// project targets EF Core 10. That is a real limit on the claim: what is verified here is that
/// QueryGuard captures and groups MySQL SQL as Oracle's provider generates it. See
/// <c>docs/decisions/0009-provider-matrix.md</c>.
/// </para>
/// <para>
/// The write cases matter most. SQL Server shipped a bug where a write was counted as a read because
/// classification looked only at the leading keyword, and MySQL emits the same shape: an
/// <c>INSERT</c> followed in the same batch by a <c>SELECT</c> that reads the row count or the
/// generated key. Both variants are asserted below rather than assumed to behave.
/// </para>
/// </remarks>
public sealed class MySqlProviderTests : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.4").Build();

    private readonly AsyncLocalQueryGuardSessionAccessor _accessor = new();

    private bool _started;

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable)
        {
            return;
        }

        await _container.StartAsync();
        _started = true;

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Created directly rather than through a second context's EnsureCreated, which is a no-op once
        // the database exists and would leave the table missing.
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS `Widgets` ("
            + "`Id` int NOT NULL AUTO_INCREMENT PRIMARY KEY, `Name` varchar(255) NOT NULL)");

        await SeedAsync(context);
    }

    public async Task DisposeAsync()
    {
        if (_started)
        {
            await _container.DisposeAsync();
        }
    }

    [DockerFact]
    public async Task A_repeated_per_parent_query_shares_one_fingerprint()
    {
        // The claim the whole product rests on, restated for MySQL: six executions differing only by
        // parameter value are one query, not six.
        await using var context = CreateContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            var ids = await context.Companies.Select(company => company.Id).ToListAsync();

            foreach (var id in ids)
            {
                _ = await context.Departments
                    .Where(department => department.CompanyId == id)
                    .ToListAsync();
            }
        }

        var departmentQueries = session.Complete().Records
            .Where(record => record.Fingerprint.NormalizedSql.Contains("Departments", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(6, departmentQueries.Count);
        Assert.Single(departmentQueries.Select(record => record.Fingerprint.Id).Distinct());
    }

    [DockerFact]
    public async Task Backtick_quoted_identifiers_are_left_alone()
    {
        // Identifier quoting is structure, not data. Rewriting `Companies` to "Companies" would make
        // the report show SQL the application never ran, so a MySQL fingerprint is deliberately not
        // interchangeable with a PostgreSQL or SQL Server one.
        await using var context = CreateContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            _ = await context.Companies.ToListAsync();
        }

        var normalized = Assert.Single(session.Complete().Records).Fingerprint.NormalizedSql;

        Assert.Contains("`Companies`", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Companies\"", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("[Companies]", normalized, StringComparison.Ordinal);
    }

    [DockerFact]
    public async Task A_parameter_reference_is_replaced_by_the_placeholder()
    {
        await using var context = CreateContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            _ = await context.Departments
                .Where(department => department.CompanyId == 1)
                .ToListAsync();
        }

        var normalized = Assert.Single(session.Complete().Records).Fingerprint.NormalizedSql;

        Assert.DoesNotContain("@", normalized, StringComparison.Ordinal);
        Assert.Contains("`Departments`", normalized, StringComparison.Ordinal);
        Assert.Contains("?", normalized, StringComparison.Ordinal);
    }

    [DockerFact]
    public async Task An_inlined_literal_is_redacted()
    {
        // MySQL inlines this constant rather than parameterizing it, which is why the assertion is
        // that the value is gone rather than that a placeholder appeared: the two providers reach the
        // same outcome by different paths, and only one of them goes through the redactor.
        await using var context = CreateContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            _ = await context.Companies.Where(company => company.City == "Paris").ToListAsync();
        }

        var normalized = Assert.Single(session.Complete().Records).Fingerprint.NormalizedSql;

        Assert.DoesNotContain("Paris", normalized, StringComparison.Ordinal);
        Assert.Contains("`City`", normalized, StringComparison.Ordinal);
    }

    [DockerFact]
    public async Task Two_different_queries_do_not_share_a_fingerprint()
    {
        await using var context = CreateContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            _ = await context.Companies.ToListAsync();
            _ = await context.Departments.ToListAsync();
        }

        Assert.Equal(
            2,
            session.Complete().Records.Select(record => record.Fingerprint.Id).Distinct().Count());
    }

    [DockerFact]
    public async Task A_write_is_not_counted_as_a_read()
    {
        // MySQL appends `SELECT ROW_COUNT()` to the insert batch. Classifying on the leading keyword
        // alone still gets this right, but the SQL Server bug was exactly this shape with a prologue
        // in front, so it is asserted rather than assumed.
        await using var context = CreateContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            context.Departments.Add(new Department { Id = 100, CompanyId = 1, Name = "Added" });
            await context.SaveChangesAsync();
        }

        var completed = session.Complete();

        Assert.NotEmpty(completed.Records);
        Assert.All(completed.Records, record => Assert.False(record.IsRead));
        Assert.Equal(0, completed.CountedCommandCount);
    }

    [DockerFact]
    public async Task A_write_whose_key_the_server_generates_is_not_counted_as_a_read()
    {
        // The case the fixed-key model above cannot reach. Reading a generated key back makes MySQL
        // emit `INSERT ... SELECT `Id` FROM ... WHERE ROW_COUNT() = ? AND `Id` = LAST_INSERT_ID()`,
        // a batch whose last statement is a SELECT. A budget of ten reads has to mean the same thing
        // here as on SQLite.
        await using var context = CreateGeneratedKeyContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            context.Widgets.Add(new Widget { Name = "First" });
            await context.SaveChangesAsync();
        }

        var completed = session.Complete();

        Assert.NotEmpty(completed.Records);
        Assert.All(completed.Records, record => Assert.False(record.IsRead));
        Assert.Equal(0, completed.CountedCommandCount);
    }

    [DockerFact]
    public async Task A_failing_command_is_recorded_and_the_mysql_exception_still_surfaces()
    {
        // Capture must never swallow the failure the application was going to see.
        await using var context = CreateContext();
        var session = NewSession();

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using (_accessor.Activate(session))
            {
                await context.Database.ExecuteSqlRawAsync("SELECT * FROM `NoSuchTable`");
            }
        });

        Assert.Contains("MySql", exception.GetType().FullName!, StringComparison.Ordinal);

        var record = Assert.Single(session.Complete().Records);
        Assert.True(record.IsFailed);
        Assert.Contains("MySql", record.FailureType!, StringComparison.Ordinal);
    }

    [DockerFact]
    public async Task A_query_tag_survives_mysql_sql_generation()
    {
        await using var context = CreateContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            _ = await context.Companies
                .TagWith("QueryGuard:Ignore reason=bounded-reference-lookup")
                .ToListAsync();
        }

        var record = Assert.Single(session.Complete().Records);

        Assert.True(QueryGuardQueryTag.HasIgnoreDirective(record.Tags));
        Assert.Equal("bounded-reference-lookup", QueryGuardQueryTag.GetIgnoreReason(record.Tags));
    }

    [DockerFact]
    public async Task A_tagged_query_reports_sql_the_tag_has_not_commented_out()
    {
        // Found here. TagWith emits a line comment, normalization collapses the line break that ended
        // it, and the directive was kept as "--" — so the reported SQL for every tagged query on every
        // provider read as entirely commented out. Asserted against a live provider because that is
        // where the shape came from.
        await using var context = CreateContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            _ = await context.Companies
                .TagWith("QueryGuard:Ignore reason=bounded-reference-lookup")
                .ToListAsync();
        }

        var normalized = Assert.Single(session.Complete().Records).Fingerprint.NormalizedSql;

        Assert.DoesNotContain("--", normalized, StringComparison.Ordinal);

        var afterDirective = normalized[(normalized.IndexOf("*/", StringComparison.Ordinal) + 2)..];
        Assert.Contains("SELECT", afterDirective, StringComparison.Ordinal);
        Assert.Contains("`Companies`", afterDirective, StringComparison.Ordinal);
    }

    private static QueryGuardSession NewSession()
        => new("provider-test", QueryGuardPolicy.Create("provider"));

    private ProviderDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ProviderDbContext>()
            .UseMySQL(_container.GetConnectionString())
            .UseQueryGuard(_accessor)
            .Options;

        return new ProviderDbContext(options);
    }

    private MySqlGeneratedKeyContext CreateGeneratedKeyContext()
    {
        var options = new DbContextOptionsBuilder<MySqlGeneratedKeyContext>()
            .UseMySQL(_container.GetConnectionString())
            .UseQueryGuard(_accessor)
            .Options;

        return new MySqlGeneratedKeyContext(options);
    }

    private static async Task SeedAsync(ProviderDbContext context)
    {
        for (var companyIndex = 1; companyIndex <= 6; companyIndex++)
        {
            context.Companies.Add(new Company
            {
                Id = companyIndex,
                Name = string.Create(CultureInfo.InvariantCulture, $"Company {companyIndex}"),
                City = companyIndex % 2 == 0 ? "Lyon" : "Paris",
            });

            context.Departments.Add(new Department
            {
                Id = companyIndex,
                CompanyId = companyIndex,
                Name = "Engineering",
            });
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }
}

/// <summary>
/// A second schema whose key the server generates, so the insert batch has to read it back.
/// </summary>
/// <remarks>
/// Separate from <see cref="ProviderDbContext"/>, which uses <c>ValueGeneratedNever</c> throughout so
/// that its fingerprints stay comparable across providers. Without a generated key nothing exercises
/// the batch shape that mixes a write and a read.
/// </remarks>
public sealed class MySqlGeneratedKeyContext : DbContext
{
    public MySqlGeneratedKeyContext(DbContextOptions<MySqlGeneratedKeyContext> options)
        : base(options)
    {
    }

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Widget>(entity =>
        {
            entity.HasKey(widget => widget.Id);
            entity.Property(widget => widget.Id).ValueGeneratedOnAdd();
            entity.Property(widget => widget.Name).IsRequired();
        });
}

/// <summary>
/// A row with a server-generated key.
/// </summary>
public sealed class Widget
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
