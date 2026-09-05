# Testing with QueryGuard

Measure an HTTP request, a service method, or a background job. Each returns a
`QueryGuardResult` that you can check with the same assertions.

## ASP.NET Core integration tests

Install:

```bash
dotnet add package QueryGuard.AspNetCore.Testing
```

Use your application's `Program` and `AppDbContext` types:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using QueryGuard;
using QueryGuard.AspNetCore.Testing;
using QueryGuard.Testing;

using var factory = new WebApplicationFactory<Program>();
await using var guard = factory.TrackQueries<Program, AppDbContext>(
    "GET /api/companies",
    QueryGuardPolicy.Create("companies")
        .WithMaxOccurrencesPerFingerprint(5));

var response = await guard.Client.GetAsync("/api/companies");
response.EnsureSuccessStatusCode();

var result = await guard.CompleteAsync();
QueryGuardAssert.Passes(result);
```

The test fails if a query runs more than five times.

`TrackQueries` attaches the interceptor and sets up the test host. It also disables request
middleware during the measurement to avoid overlapping scopes.

Use **`guard.Client`** to send requests. Choose the `DbContext` you want to measure.
Use separate measurements for contexts that need separate budgets.

## Services and background jobs

Install:

```bash
dotnet add package QueryGuard.Testing
```

Attach QueryGuard where you configure the context:

```csharp
using QueryGuard.EntityFrameworkCore;

options.UseSqlite(connectionString).UseQueryGuard();
```

Open a scope around the code under test:

```csharp
using QueryGuard;
using QueryGuard.Testing;

await using var scope = QueryGuardScope.Start(
    "refresh company summary",
    QueryGuardPolicy.Create("company-summary")
        .WithMaxQueries(3)
        .WithMaxOccurrencesPerFingerprint(1));

await service.RefreshCompanySummaryAsync();

var result = await scope.CompleteAsync();
QueryGuardAssert.Passes(result);
```

This allows up to three counted commands, with no repeated query.
`QueryGuard.Testing` includes the EF Core integration and works with any test framework.

## Start with a baseline when the budget is unknown

Save the current result and compare later runs. This example uses xUnit:

```csharp
var baseline = QueryGuardBaseline.FromJson(await File.ReadAllTextAsync("queryguard-baseline.json"));
var comparison = QueryGuardBaselineComparison.Compare(baseline, [result]);

Assert.Empty(comparison.Regressions);
```

See [baselines](../baselines/README.md) to create the file and use it in CI.

## Common failures

| Symptom | Check |
| --- | --- |
| Zero commands from a request | Use `TrackQueries` and its `Client` |
| Unexpected command counts | Check for duplicate interceptors or overlapping scopes |
| Commands appear in the wrong scope | Complete one measurement before opening the next one |
| Report missing in CI | Use a report path based on the repository root |

See [troubleshooting](../troubleshooting/README.md) for manual setup.