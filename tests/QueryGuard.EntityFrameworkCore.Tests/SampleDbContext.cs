using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

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
/// An in-memory SQLite database that lives as long as the fixture holds its connection.
/// </summary>
/// <remarks>
/// SQLite in shared-cache memory mode gives real relational command execution — real SQL generation,
/// real parameters, real provider behavior — with no container to start and no file to clean up. The
/// connection has to be held open, because the database ceases to exist when the last connection to
/// it closes.
/// </remarks>
public sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _isDisposed;

    public SqliteFixture(int companies = 5, int departmentsPerCompany = 3)
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

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
            .UseSqlite(_connection);

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
        _connection.Dispose();
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
