using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueryGuard.EntityFrameworkCore;

namespace QueryGuard.AspNetCore.Tests;

/// <summary>
/// A minimal API over a synthetic schema, hosted in memory.
/// </summary>
/// <remarks>
/// Deliberately small and deliberately containing one endpoint that queries per parent row. Testing
/// middleware against a mock pipeline would not prove that a real EF Core query, executed inside a real
/// request, lands in the right session.
/// </remarks>
internal sealed class SampleApplication : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly SqliteConnection _keepAlive;
    private bool _isDisposed;

    private SampleApplication(IHost host, SqliteConnection keepAlive, InMemoryLogSink logs)
    {
        _host = host;
        _keepAlive = keepAlive;
        Logs = logs;
    }

    internal InMemoryLogSink Logs { get; }

    internal HttpClient CreateClient() => _host.GetTestClient();

    internal static async Task<SampleApplication> StartAsync(
        Action<QueryGuardOptions>? configure = null,
        bool withQueryGuard = true)
    {
        var connectionString = string.Create(
            CultureInfo.InvariantCulture,
            $"Data Source=queryguard-aspnet-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

        var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        var logs = new InMemoryLogSink();

        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureWebHost(web =>
        {
            web.UseTestServer();

            web.ConfigureServices(services =>
            {
                services.AddLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddProvider(logs);

                    // Everything QueryGuard says, and only what the framework genuinely complains
                    // about. Capturing framework Debug output as well drowns the assertions and slows
                    // every test down for no benefit.
                    logging.SetMinimumLevel(LogLevel.Warning);
                    logging.AddFilter("QueryGuard", LogLevel.Debug);
                });

                services.AddRouting();

                if (withQueryGuard)
                {
                    services.AddQueryGuard(options =>
                    {
                        options.DefaultPolicy = QueryGuardPolicy.Create("default");
                        configure?.Invoke(options);
                    });
                }

                services.AddDbContext<SampleDbContext>((provider, db) =>
                {
                    db.UseSqlite(connectionString);

                    if (withQueryGuard)
                    {
                        db.AddInterceptors(provider.GetRequiredService<QueryGuardCommandInterceptor>());
                    }
                });
            });

            web.Configure(app =>
            {
                app.UseRouting();

                if (withQueryGuard)
                {
                    // After UseRouting, so the scope name comes from the matched route pattern.
                    app.UseQueryGuard();
                }

                app.UseEndpoints(endpoints =>
                {
                    // The problem endpoint: one child query per parent row.
                    endpoints.MapGet("/api/companies", static async (SampleDbContext db) =>
                    {
                        var companies = await db.Companies.AsNoTracking().ToListAsync();
                        var payload = new List<object>(companies.Count);

                        foreach (var company in companies)
                        {
                            var departments = await db.Departments
                                .AsNoTracking()
                                .Where(department => department.CompanyId == company.Id)
                                .ToListAsync();

                            payload.Add(new { company.Id, company.Name, Departments = departments.Count });
                        }

                        return Results.Ok(payload);
                    });

                    // The same data, one query.
                    endpoints.MapGet("/api/companies/projected", static async (SampleDbContext db) =>
                    {
                        var payload = await db.Companies
                            .AsNoTracking()
                            .Select(company => new { company.Id, company.Name, Departments = company.Departments.Count })
                            .ToListAsync();

                        return Results.Ok(payload);
                    });

                    endpoints.MapGet("/api/companies/{id:int}", static async (int id, SampleDbContext db) =>
                    {
                        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
                        return company is null ? Results.NotFound() : Results.Ok(new { company.Id, company.Name });
                    });

                    endpoints.MapGet("/api/boom", static async (SampleDbContext db) =>
                    {
                        _ = await db.Companies.AsNoTracking().CountAsync();
                        throw new InvalidOperationException("simulated endpoint failure");
                    });

                    endpoints.MapGet("/health", static () => Results.Ok("healthy"));
                });
            });
        });

        var host = await builder.StartAsync();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            await db.Database.EnsureCreatedAsync();
            Seed(db);
        }

        logs.Clear();
        return new SampleApplication(host, keepAlive, logs);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await _host.StopAsync();
        _host.Dispose();
        await _keepAlive.DisposeAsync();
    }

    private static void Seed(SampleDbContext db)
    {
        if (db.Companies.Any())
        {
            return;
        }

        for (var companyIndex = 1; companyIndex <= 6; companyIndex++)
        {
            db.Companies.Add(new Company
            {
                Id = companyIndex,
                Name = string.Create(CultureInfo.InvariantCulture, $"Company {companyIndex}"),
            });

            for (var departmentIndex = 1; departmentIndex <= 2; departmentIndex++)
            {
                db.Departments.Add(new Department
                {
                    Id = ((companyIndex - 1) * 2) + departmentIndex,
                    CompanyId = companyIndex,
                    Name = string.Create(CultureInfo.InvariantCulture, $"Department {departmentIndex}"),
                });
            }
        }

        db.SaveChanges();
        db.ChangeTracker.Clear();
    }
}

internal sealed class SampleDbContext : DbContext
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

internal sealed class Company
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<Department> Departments { get; } = [];
}

internal sealed class Department
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public string Name { get; set; } = string.Empty;

    public Company? Company { get; set; }
}
