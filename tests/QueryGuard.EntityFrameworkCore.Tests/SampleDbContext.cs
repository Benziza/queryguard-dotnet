using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace QueryGuard.EntityFrameworkCore.Tests;

/// <summary>
/// A synthetic schema: companies with departments, and nothing else.
/// </summary>
/// <remarks>
/// Two entities related one-to-many is the minimum needed to produce a genuine repeated-query
/// pattern — one child query per parent row. Everything here is invented; no schema, name, or SQL in
/// this repository comes from a real application.
/// </remarks>
public sealed class SampleDbContext : DbContext
{
    public SampleDbContext(DbContextOptions<SampleDbContext> options)
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
            entity.Property(company => company.Name).IsRequired();
            entity
                .HasMany(company => company.Departments)
                .WithOne(department => department.Company!)
                .HasForeignKey(department => department.CompanyId);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(department => department.Id);
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

    public Company? Company { get; set; }
}

/// <summary>
/// A real SQLite database, held in memory for the lifetime of the fixture.
/// </summary>
/// <remarks>
/// <para>
/// SQLite gives real relational command execution — real SQL generation, real parameters, real
/// provider behavior — with no container to start and no file to clean up.
/// </para>
/// <para>
/// It uses a <em>named</em> shared-cache in-memory database rather than a bare <c>:memory:</c>
/// connection, so that each context can open its own connection to the same data. Sharing a single
/// <see cref="SqliteConnection"/> across contexts is not safe when commands run concurrently, and
/// the concurrency tests here exist precisely to run commands concurrently. One connection is held
/// open for the fixture's lifetime because a shared in-memory database ceases to exist when the last
/// connection to it closes.
/// </para>
/// <para>
/// The database name includes a GUID so that parallel test classes never collide.
/// </para>
/// </remarks>
public sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;
    private bool _isDisposed;

    public SqliteFixture(int companies = 5, int departmentsPerCompany = 3)
    {
        _connectionString = string.Create(
            CultureInfo.InvariantCulture,
            $"Data Source=queryguard-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
        Seed(context, companies, departmentsPerCompany);
    }

    /// <summary>
    /// Creates a context over the fixture's database, optionally with interceptors attached.
    /// </summary>
    public SampleDbContext CreateContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite(_connectionString);

        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new SampleDbContext(builder.Options);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _keepAlive.Dispose();
    }

    private static void Seed(SampleDbContext context, int companies, int departmentsPerCompany)
    {
        // Deterministic seed data, so a demo or an assertion never depends on random values.
        for (var companyIndex = 1; companyIndex <= companies; companyIndex++)
        {
            var company = new Company
            {
                Id = companyIndex,
                Name = string.Create(CultureInfo.InvariantCulture, $"Company {companyIndex}"),
                City = companyIndex % 2 == 0 ? "Lyon" : "Paris",
            };

            context.Companies.Add(company);

            for (var departmentIndex = 1; departmentIndex <= departmentsPerCompany; departmentIndex++)
            {
                context.Departments.Add(new Department
                {
                    Id = ((companyIndex - 1) * departmentsPerCompany) + departmentIndex,
                    CompanyId = companyIndex,
                    Name = string.Create(CultureInfo.InvariantCulture, $"Department {departmentIndex}"),
                });
            }
        }

        context.SaveChanges();
        context.ChangeTracker.Clear();
    }
}
