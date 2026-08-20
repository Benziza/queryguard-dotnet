using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace QueryGuard.SampleApi;

/// <summary>
/// A synthetic catalog: companies, each with a few departments.
/// </summary>
/// <remarks>
/// Two entities in a one-to-many relationship is the smallest schema that can produce a genuine
/// repeated-query pattern — one child query per parent row. Everything here is invented. No schema,
/// name, or SQL in this repository comes from a real application.
/// </remarks>
public sealed class CatalogDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogDbContext"/> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets the companies.
    /// </summary>
    public DbSet<Company> Companies => Set<Company>();

    /// <summary>
    /// Gets the departments.
    /// </summary>
    public DbSet<Department> Departments => Set<Department>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(company => company.Id);
            entity.Property(company => company.Name).IsRequired().HasMaxLength(200);
            entity.Property(company => company.City).IsRequired().HasMaxLength(100);
            entity
                .HasMany(company => company.Departments)
                .WithOne(department => department.Company!)
                .HasForeignKey(department => department.CompanyId);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(department => department.Id);
            entity.Property(department => department.Name).IsRequired().HasMaxLength(200);
        });
    }

    /// <summary>
    /// Creates the schema and fills it with deterministic sample data.
    /// </summary>
    /// <param name="companies">How many companies to create.</param>
    /// <param name="departmentsPerCompany">How many departments each company gets.</param>
    /// <remarks>
    /// Deterministic on purpose: the README quotes exact query counts, and a demo whose numbers move
    /// between runs is a demo nobody trusts.
    /// </remarks>
    public void Seed(int companies = 50, int departmentsPerCompany = 3)
    {
        Database.EnsureCreated();

        if (Companies.Any())
        {
            return;
        }

        for (var companyIndex = 1; companyIndex <= companies; companyIndex++)
        {
            Companies.Add(new Company
            {
                Id = companyIndex,
                Name = string.Create(CultureInfo.InvariantCulture, $"Company {companyIndex:000}"),
                City = (companyIndex % 3) switch
                {
                    0 => "Lyon",
                    1 => "Paris",
                    _ => "Nantes",
                },
            });

            for (var departmentIndex = 1; departmentIndex <= departmentsPerCompany; departmentIndex++)
            {
                Departments.Add(new Department
                {
                    Id = ((companyIndex - 1) * departmentsPerCompany) + departmentIndex,
                    CompanyId = companyIndex,
                    Name = string.Create(CultureInfo.InvariantCulture, $"Department {departmentIndex}"),
                });
            }
        }

        SaveChanges();
        ChangeTracker.Clear();
    }
}

/// <summary>
/// A company in the sample catalog.
/// </summary>
public sealed class Company
{
    /// <summary>Gets or sets the identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the city.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>Gets the departments belonging to this company.</summary>
    public ICollection<Department> Departments { get; } = [];
}

/// <summary>
/// A department belonging to a <see cref="Company"/>.
/// </summary>
public sealed class Department
{
    /// <summary>Gets or sets the identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the owning company's identifier.</summary>
    public int CompanyId { get; set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the owning company.</summary>
    public Company? Company { get; set; }
}

/// <summary>
/// What the sample endpoints return.
/// </summary>
/// <param name="Id">The company identifier.</param>
/// <param name="Name">The company name.</param>
/// <param name="City">The city.</param>
/// <param name="DepartmentCount">How many departments the company has.</param>
public sealed record CompanySummary(int Id, string Name, string City, int DepartmentCount);
