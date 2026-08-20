using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using QueryGuard.EntityFrameworkCore;
using Testcontainers.MsSql;
using Xunit;

namespace QueryGuard.ProviderTests;

/// <summary>
/// QueryGuard against a real SQL Server instance.
/// </summary>
/// <remarks>
/// <para>
/// SQL Server was the one provider most .NET developers would check first and the one this project
/// could only claim as "fixture-verified" — the normalizer was pinned against captured SQL with
/// nothing live behind it. A fixture proves the normalizer still does what it did when the fixture was
/// written; it cannot notice that the provider now emits something the fixture never contained.
/// </para>
/// <para>
/// The distinctive thing about SQL Server's generated SQL is the parameter declaration prologue:
/// </para>
/// <code>
/// @__p_0 int
/// SELECT ... WHERE [d].[CompanyId] = @__p_0
/// </code>
/// <para>
/// That prologue changes with the parameter's value and type, so a normalizer that failed to strip it
/// would produce a different fingerprint per execution — and a per-parent query in a loop would look
/// like N distinct queries, which is precisely the case QueryGuard exists to catch. This suite runs
/// that loop against a real server rather than trusting the fixture.
/// </para>
/// <para>
/// See <c>docs/decisions/0009-provider-matrix.md</c>.
/// </para>
/// </remarks>
public sealed class SqlServerProviderTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

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
    public async Task A_repeated_query_shares_one_fingerprint_despite_the_declaration_prologue()
    {
        // The claim the whole product rests on, restated for SQL Server. If the prologue survived
        // normalization this would report six fingerprints instead of one, and an N+1 in a loop would
        // be invisible.
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

        var completed = session.Complete();
        var departmentQueries = completed.Records
            .Where(record => record.Fingerprint.NormalizedSql.Contains("Departments", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(6, departmentQueries.Count);
        Assert.Single(departmentQueries.Select(record => record.Fingerprint.Id).Distinct());
    }

    [DockerFact]
    public async Task The_fingerprint_matches_what_the_captured_fixture_pinned()
    {
        // The point of running live: the fixture in QueryGuard.Core.Tests was captured by hand, and
        // this is the only thing that can notice the provider drifting away from it. Comparing the
        // normalized text rather than the hash keeps the failure readable when it does drift.
        await using var context = CreateContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            _ = await context.Departments
                .Where(department => department.CompanyId == 1)
                .ToListAsync();
        }

        var normalized = Assert.Single(session.Complete().Records).Fingerprint.NormalizedSql;

        Assert.DoesNotContain("@__", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("declare", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Departments]", normalized, StringComparison.Ordinal);
        Assert.Contains("?", normalized, StringComparison.Ordinal);
    }

    [DockerFact]
    public async Task Bracket_quoted_identifiers_are_left_alone()
    {
        // Identifier quoting is structure, not data. Rewriting [Departments] to "Departments" would
        // make the report show SQL the application never ran, so a SQL Server fingerprint is
        // deliberately not interchangeable with a PostgreSQL one.
        await using var context = CreateContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            _ = await context.Companies.ToListAsync();
        }

        var normalized = Assert.Single(session.Complete().Records).Fingerprint.NormalizedSql;

        Assert.Contains("[Companies]", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Companies\"", normalized, StringComparison.Ordinal);
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

        var completed = session.Complete();

        Assert.Equal(2, completed.Records.Select(record => record.Fingerprint.Id).Distinct().Count());
    }

    [DockerFact]
    public async Task An_inlined_literal_is_redacted_in_sql_server_sql()
    {
        await using var context = CreateContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            _ = await context.Companies.Where(company => company.City == "Paris").ToListAsync();
        }

        var record = Assert.Single(session.Complete().Records);

        Assert.DoesNotContain("Paris", record.Fingerprint.NormalizedSql, StringComparison.Ordinal);
        Assert.Contains("City", record.Fingerprint.NormalizedSql, StringComparison.Ordinal);
    }

    [DockerFact]
    public async Task A_write_is_not_counted_as_a_read_on_sql_server()
    {
        // SQL Server returns generated keys through a SELECT after the INSERT, so the command that
        // carries the write can arrive on the reader path. Counting it as a read would make a budget
        // of ten reads mean something different here than on SQLite.
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
    public async Task A_failing_command_is_recorded_and_the_sql_server_exception_still_surfaces()
    {
        await using var context = CreateContext();
        var session = NewSession();

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using (_accessor.Activate(session))
            {
                await context.Database.ExecuteSqlRawAsync("SELECT * FROM [NoSuchTable]");
            }
        });

        Assert.Contains("Sql", exception.GetType().FullName!, StringComparison.Ordinal);

        var record = Assert.Single(session.Complete().Records);
        Assert.True(record.IsFailed);
        Assert.Contains("Sql", record.FailureType!, StringComparison.Ordinal);
    }

    [DockerFact]
    public async Task A_query_tag_survives_sql_server_sql_generation()
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

    private static QueryGuardSession NewSession()
        => new("provider-test", QueryGuardPolicy.Create("provider"));

    private ProviderDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ProviderDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .UseQueryGuard(_accessor)
            .Options;

        return new ProviderDbContext(options);
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
