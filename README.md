<p align="center">
  <img src="./docs/assets/queryguard-logo.svg" width="112" alt="QueryGuard.NET logo">
</p>

<h1 align="center">QueryGuard.NET</h1>

<p align="center">
  Find repeated EF Core queries and catch query problems in tests.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/QueryGuard.Testing">
    <img alt="NuGet" src="https://img.shields.io/nuget/vpre/QueryGuard.Testing?color=004880&label=nuget">
  </a>
  <a href="https://github.com/Benziza/queryguard-dotnet/actions/workflows/ci.yml">
    <img alt="CI" src="https://github.com/Benziza/queryguard-dotnet/actions/workflows/ci.yml/badge.svg">
  </a>
  <a href="https://benziza.github.io/queryguard-dotnet/">
    <img alt="Documentation" src="https://img.shields.io/badge/docs-benziza.github.io-2088FF.svg">
  </a>
  <a href="./LICENSE">
    <img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-blue.svg">
  </a>
</p>

QueryGuard counts database queries and checks them against limits you set.
For example, you can fail a test when the same query runs more than 5 times.

Repeated queries can be a sign of an N+1 problem. Some repeats are expected,
so you can allow them with a reason.

## Quick start

Add this package to your ASP.NET Core test project:

```bash
dotnet add package QueryGuard.AspNetCore.Testing
```

Run a request and check its queries:

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

QueryGuardAssert.Passes(await guard.CompleteAsync());
```

Replace `Program`, `AppDbContext`, and the route with your app's values.
This test fails if a query with the same SQL pattern runs more than 5 times.

Works with xUnit, NUnit, MSTest, and TUnit.
For services and background jobs, see the [testing guide](./docs/testing/README.md).

## Packages

| Package | Use |
| --- | --- |
| `QueryGuard.AspNetCore.Testing` | Test ASP.NET Core requests |
| `QueryGuard.Testing` | Test services and background jobs |
| `QueryGuard.AspNetCore` | Track queries during HTTP requests |
| `QueryGuard.Reporting` | Export console, JSON, JUnit, Markdown, or SARIF reports |
| `QueryGuard.Cli` | Save query counts and check for changes in CI |

## Support

- .NET 8 and 10, with EF Core 8 and 10.
- Tested with SQLite, PostgreSQL, SQL Server, and MySQL. See [provider details](./docs/providers/README.md).
- EF Core only. Dapper and raw ADO.NET are not tracked.
- Parameter values and connection strings are not captured. SQL is redacted before reporting.

## Try the sample

```bash
git clone https://github.com/Benziza/queryguard-dotnet.git
cd queryguard-dotnet
dotnet test samples/QueryGuard.SampleTests
```

The sample shows a request that runs 51 queries and a fix that runs just one.

## Learn more

- [Documentation and API reference](https://benziza.github.io/queryguard-dotnet/)
- [Query limits and settings](./docs/configuration/README.md)
- [Save and compare query counts in CI](./docs/baselines/README.md)
- [GitHub Action](./action/README.md)
- [Troubleshooting](./docs/troubleshooting/README.md)
- [Contributing](./CONTRIBUTING.md) · [Report a security issue](./SECURITY.md)

## License

[MIT](./LICENSE). Inspired by [Bullet](https://github.com/flyerhzm/bullet) for Rails.
