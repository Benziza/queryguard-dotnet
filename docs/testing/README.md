# Testing with QueryGuard

QueryGuard can measure a real ASP.NET Core request or a smaller block of application code. Both paths
produce the same `QueryGuardResult` and use the same assertions.

## ASP.NET Core integration tests

Install the helper package:

```bash
dotnet add package QueryGuard.AspNetCore.Testing
```

Measure a request made through `WebApplicationFactory`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
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

`TrackQueries` handles the setup that is easy to miss in an integration test:

- it attaches QueryGuard to the selected `DbContext`
- it sets `TestServerOptions.PreserveExecutionContext`
- it uses the session accessor from the hosted application
- it disables QueryGuard request middleware for the measurement, so there is only one active scope
- it avoids adding the interceptor twice when the application already uses QueryGuard

Use the client exposed by `guard`. A client created directly from the original factory does not use the
configured test host.

If the application uses more than one context, choose the context whose commands the test should
measure. Open separate measurements when a test needs separate budgets for separate contexts.

## Services and background jobs

Install the general testing package:

```bash
dotnet add package QueryGuard.Testing
```

Attach QueryGuard when the context is configured:

```csharp
options.UseSqlite(connectionString).UseQueryGuard();
```

Then open a scope around the code under test:

```csharp
await using var scope = QueryGuardScope.Start(
    "refresh company summary",
    QueryGuardPolicy.Create("company-summary")
        .WithMaxQueries(3)
        .WithMaxOccurrencesPerFingerprint(1));

await service.RefreshCompanySummaryAsync();

var result = await scope.CompleteAsync();
QueryGuardAssert.Passes(result);
```

`QueryGuard.Testing` brings the EF Core integration with it. It does not reference xUnit, NUnit,
MSTest, or TUnit.

## Start with a baseline when the budget is unknown

A new test often has no agreed query budget. Record the current result first and compare later runs:

```csharp
var baseline = QueryGuardBaseline.FromJson(await File.ReadAllTextAsync("queryguard-baseline.json"));
var comparison = QueryGuardBaselineComparison.Compare(baseline, [result]);

Assert.Empty(comparison.Regressions);
```

See [baselines](../baselines/README.md) for recording the file and publishing the comparison in CI.

## Common failures

| Symptom | Check |
| --- | --- |
| Zero commands from a `WebApplicationFactory` request | Use `TrackQueries` and its `Client` |
| Twice the expected command count | Do not run request middleware inside an explicit test measurement |
| Commands appear in the wrong scope | Complete one measurement before opening the next one |
| The test passes but the report is missing in CI | Write reports to a path anchored at the repository root |

See [troubleshooting](../troubleshooting/README.md) for manual session wiring and lower-level details.
