using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QueryGuard;
using QueryGuard.AspNetCore;
using QueryGuard.EntityFrameworkCore;
using QueryGuard.SampleApi;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// QueryGuard
// ---------------------------------------------------------------------------
builder.Services.AddQueryGuard(options =>
{
    // The recommended posture for the first preview: development and test only. QueryGuard observes
    // the database command path, and enabling that in production should be a deliberate act.
    options.Enabled = builder.Environment.IsDevelopment();

    // A warning, not a failure. Installing QueryGuard should tell you what it sees before it starts
    // failing anything: that is what makes it safe to add to an existing project.
    options.DefaultPolicy = QueryGuardPolicy.Create("default")
        .WithMaxQueries(20, QueryGuardSeverity.Warning)
        .WithRepeatedQueryThreshold(3);

    // The endpoint this sample exists to demonstrate. A per-fingerprint budget is the rule that
    // actually catches an N+1: a total-count budget can stay satisfied while one query quietly repeats.
    options.ForEndpoint(
        "GET /api/companies",
        policy => policy.WithMaxOccurrencesPerFingerprint(5, QueryGuardSeverity.Failure));

    // Logged even when clean, so running the sample shows a summary for every request rather than
    // only for the broken one. Not a default: on a real application this is noise on every request.
    options.LogSummaryWhenClean = true;
});

// ---------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Catalog")
    ?? "Data Source=queryguard-sample.db";

builder.Services.AddDbContext<CatalogDbContext>((provider, options) =>
{
    options.UseSqlite(connectionString);

    // This one line is what connects QueryGuard to EF Core. The interceptor is a singleton and holds
    // no request state; it finds the active scope through the session accessor.
    options.AddInterceptors(provider.GetRequiredService<QueryGuardCommandInterceptor>());
});

builder.Services.AddRouting();
builder.Logging.AddSimpleConsole(console => console.SingleLine = false);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Seed();
}

app.UseRouting();

// After UseRouting, so the scope name comes from the matched route pattern rather than the URL.
app.UseQueryGuard();

// ---------------------------------------------------------------------------
// The problem
// ---------------------------------------------------------------------------

// Returns 200 OK with correct data, and executes one query per company to do it. This is the shape of
// regression that survives code review: nothing about the response is wrong.
app.MapGet("/api/companies", async (CatalogDbContext db) =>
{
    var companies = await db.Companies.AsNoTracking().ToListAsync();
    var summaries = new List<CompanySummary>(companies.Count);

    foreach (var company in companies)
    {
        // One extra round trip per row. With 50 companies that is 51 queries.
        var departmentCount = await db.Departments
            .AsNoTracking()
            .CountAsync(department => department.CompanyId == company.Id);

        summaries.Add(new CompanySummary(company.Id, company.Name, company.City, departmentCount));
    }

    return Results.Ok(summaries);
});

// ---------------------------------------------------------------------------
// The fix
// ---------------------------------------------------------------------------

// The same response, from one query. Projection lets the database do the counting.
app.MapGet("/api/companies/projected", async (CatalogDbContext db) =>
{
    var summaries = await db.Companies
        .AsNoTracking()
        .Select(company => new CompanySummary(
            company.Id,
            company.Name,
            company.City,
            company.Departments.Count))
        .ToListAsync();

    return Results.Ok(summaries);
});

// A bounded repetition that is intentional: three lookups, capped by the shape of the report rather
// than by the number of rows. The tag documents that where the query is written.
app.MapGet("/api/reports/summary", async (CatalogDbContext db) =>
{
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var city in SampleData.ReportCities)
    {
        counts[city] = await db.Companies
            .AsNoTracking()
            .TagWith("QueryGuard:Ignore reason=three-city-report-sections-bounded-by-layout")
            .CountAsync(company => company.City == city);
    }

    return Results.Ok(counts);
});

app.MapGet("/", () => Results.Ok(new
{
    message = "QueryGuard sample. Compare the two endpoints and watch the log.",
    endpoints = SampleData.EndpointDescriptions,
}));

await app.RunAsync();

/// <summary>
/// Values the endpoints read on every request.
/// </summary>
/// <remarks>
/// Held as static fields rather than built inline: a minimal-API handler runs per request, and
/// allocating the same constant array each time is waste an analyzer is right to flag.
/// </remarks>
internal static class SampleData
{
    /// <summary>
    /// The cities the summary report covers. Three, fixed by the shape of the report.
    /// </summary>
    internal static readonly string[] ReportCities = ["Paris", "Lyon", "Nantes"];

    /// <summary>
    /// What the root endpoint tells a reader to try.
    /// </summary>
    internal static readonly string[] EndpointDescriptions =
    [
        "GET /api/companies            (the problem: 200 OK, 51 queries)",
        "GET /api/companies/projected  (the fix: 200 OK, 1 query)",
        "GET /api/reports/summary      (an intentional repetition, documented with a tag)",
    ];
}
