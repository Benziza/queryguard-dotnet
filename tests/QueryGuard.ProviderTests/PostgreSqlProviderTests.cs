using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace QueryGuard.ProviderTests;

/// <summary>
/// QueryGuard against a real PostgreSQL server.
/// </summary>
/// <remarks>
/// <para>
/// SQLite proves the design works. PostgreSQL proves it is not accidentally SQLite-shaped. Npgsql
/// generates positional <c>$1</c> parameters and casts identifiers differently, which is exactly the
/// class of difference that would hide in a single-provider suite, and if the normalizer failed to
/// group Npgsql's SQL, a per-parent query in a loop would look like N distinct queries and QueryGuard
/// would report nothing at all.
/// </para>
/// <para>
/// See <c>docs/decisions/0009-provider-matrix.md</c>.
/// </para>
/// </remarks>
public sealed class PostgreSqlProviderTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("queryguard")
        .WithUsername("queryguard")
        .WithPassword("queryguard")
        .Build();

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
            // Explicit, so a container is never left behind when a test fails. Testcontainers reaps
            // orphans, but relying on that turns a failed run into someone else's resource problem.
            await _container.DisposeAsync();
        }
    }

    [DockerFact]
    public async Task A_repeated_query_shares_one_fingerprint_despite_positional_parameters()
    {
        // The assertion the whole product rests on, restated for Npgsql: $1 and $3 are the same query.
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
    public async Task Postgres_sql_does_not_share_a_fingerprint_with_a_different_query()
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
    public async Task An_inlined_literal_is_redacted_in_postgres_sql()
    {
        // Npgsql inlines a constant just as SQLite does, so the same leak is possible and the same
        // redaction has to catch it.
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
    public async Task A_write_is_not_counted_as_a_read_on_postgres()
    {
        // The provider-dependent classification from the EF Core interceptor, checked on a second
        // provider: PostgreSQL uses RETURNING for generated keys as well.
        await using var context = CreateContext();
        var session = NewSession();

        using (_accessor.Activate(session))
        {
            context.Departments.Add(new Department { CompanyId = 1, Name = "Added" });
            await context.SaveChangesAsync();
        }

        var completed = session.Complete();

        Assert.NotEmpty(completed.Records);
        Assert.All(completed.Records, record => Assert.False(record.IsRead));
        Assert.Equal(0, completed.CountedCommandCount);
    }

    [DockerFact]
    public async Task A_failing_command_is_recorded_and_the_npgsql_exception_still_surfaces()
    {
        await using var context = CreateContext();
        var session = NewSession();

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using (_accessor.Activate(session))
            {
                await context.Database.ExecuteSqlRawAsync("SELECT * FROM \"NoSuchTable\"");
            }
        });

        Assert.Contains("Npgsql", exception.GetType().FullName!, StringComparison.Ordinal);

        var record = Assert.Single(session.Complete().Records);
        Assert.True(record.IsFailed);
        Assert.Contains("Npgsql", record.FailureType!, StringComparison.Ordinal);
    }

    [DockerFact]
    public async Task A_query_tag_survives_npgsql_sql_generation()
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
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(new EntityFrameworkCore.QueryGuardCommandInterceptor(
                _accessor,
                new QueryFingerprintFactory()))
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

/// <summary>
/// The synthetic schema the provider suite runs against.
/// </summary>
public sealed class ProviderDbContext : DbContext
{
    public ProviderDbContext(DbContextOptions<ProviderDbContext> options)
        : base(options)
    {
    }

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Department> Departments => Set<Department>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(company => company.Id);
            entity.Property(company => company.Id).ValueGeneratedNever();
            entity.Property(company => company.Name).IsRequired();
            entity.Property(company => company.City).IsRequired();
            entity
                .HasMany(company => company.Departments)
                .WithOne()
                .HasForeignKey(department => department.CompanyId);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(department => department.Id);
            entity.Property(department => department.Id).ValueGeneratedNever();
            entity.Property(department => department.Name).IsRequired();
        });
    }
}

public sealed class Company
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public ICollection<Department> Departments { get; } = [];
}

public sealed class Department
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public string Name { get; set; } = string.Empty;
}
