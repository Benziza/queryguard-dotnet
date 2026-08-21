<p align="center">
  <img src="./docs/assets/queryguard-logo.svg" width="112" alt="QueryGuard.NET logo">
</p>

<h1 align="center">QueryGuard.NET</h1>

<p align="center">
  <strong>Your endpoint returns 200 OK. It also ran the same query 51 times.</strong>
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
  <a href="https://dotnet.microsoft.com/en-us/platform/support/policy">
    <img alt="Targets .NET 8 and .NET 10" src="https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4.svg">
  </a>
</p>

<p align="center">
  <img src="./docs/assets/queryguard-demo.svg" width="820" alt="QueryGuard fails a query budget for GET /api/companies after one fingerprint runs 50 times.">
</p>

QueryGuard counts the EF Core queries that your code runs, groups repeated SQL, and lets a test fail
before an N+1 regression reaches production.

It reports repeated-query candidates, not proof. Some repetition is correct. Intentional cases stay
visible with a written reason.

## Quick start

For an ASP.NET Core integration test, install the WebApplicationFactory helper:

```bash
dotnet add package QueryGuard.AspNetCore.Testing --prerelease
```

Open a measurement, run the request, and assert the result:

```csharp
using var factory = new WebApplicationFactory<Program>();
await using var guard = factory.TrackQueries<Program, AppDbContext>(
    "GET /api/companies",
    QueryGuardPolicy.Create("companies")
        .WithMaxOccurrencesPerFingerprint(5));

var response = await guard.Client.GetAsync("/api/companies");
response.EnsureSuccessStatusCode();

QueryGuardAssert.Passes(await guard.CompleteAsync());
```

The helper attaches QueryGuard to `AppDbContext`, preserves the test execution context, and prevents
request middleware from opening a second scope. It has no test framework dependency, so it works with
xUnit, NUnit, MSTest, and TUnit.

Testing a service or background job without `WebApplicationFactory`? Install `QueryGuard.Testing`,
attach `.UseQueryGuard()` where the context is configured, and open an explicit `QueryGuardScope`.
See the [testing guide](./docs/testing/README.md) for both paths.

| Package | Use it for |
| --- | --- |
| `QueryGuard.AspNetCore.Testing` | Measuring real `WebApplicationFactory` requests |
| `QueryGuard.Testing` | Explicit scopes and assertions around services or jobs |
| `QueryGuard.AspNetCore` | Request middleware and route policies |
| `QueryGuard.Reporting` | Console, JSON, JUnit, Markdown, and SARIF output |
| `QueryGuard.Cli` | Recording and checking baselines in CI |

## Useful failures

```text
QueryGuard FAILED: GET /api/companies (policy 'companies')
  51 read queries in 2 distinct queries

  [FAIL] max-occurrences-per-fingerprint: QG-FP-FDB5F469 executed 50 times; the budget is 5.
          SQL: SELECT COUNT(*) FROM "Departments" AS "d" WHERE "d"."CompanyId" = ?
          origin: samples/QueryGuard.SampleApi/Program.cs:line 89
```

The report shows the repeated fingerprint, normalized SQL, and the application line that ran it.
Stack traces are captured once per distinct query in explicit test scopes. They stay off by default
on request paths because they are much more expensive than normal capture.

## Baselines

If you do not know the right budget yet, record current behavior and compare it in CI:

```bash
dotnet tool install -g QueryGuard.Cli --prerelease

queryguard baseline record
queryguard verify --summary artifacts/queryguard/summary.md
```

Add `--fail-on-regression` when a regression should fail the build. Without it, the tool reports the
change and exits successfully.

Publish the Markdown table as a job summary and sticky pull request comment:

```yaml
- uses: Benziza/queryguard-dotnet@v0.1.0-preview.6
  with:
    summary-path: artifacts/queryguard/summary.md
```

See the [baseline guide](./docs/baselines/README.md) and [action guide](./action/README.md).

## Support matrix

| Component | Support |
| --- | --- |
| .NET | .NET 8 and .NET 10 |
| EF Core | EF Core 8 and EF Core 10 |
| Providers tested with real databases | SQLite, PostgreSQL, SQL Server, MySQL |
| Other providers | Any relational EF Core provider through `DbCommandInterceptor` |

MySQL tests use Oracle's `MySql.EntityFrameworkCore`. Pomelo does not have an EF Core 10 release yet.
See [provider support](./docs/providers/README.md) for the exact claim and current caveats.

## Tested in public projects

`0.1.0-preview.6` was added to three public ASP.NET Core test suites before the stable release:

| Project | Test stack | Request | Result |
| --- | --- | --- | --- |
| [CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture/tree/10f1a45df0d86bb87b083f3a0e249d755093fbbd) | NUnit, SQLite | `POST /api/Users/register` | 1 query, 1 group |
| [SSW.VerticalSliceArchitecture](https://github.com/SSWConsulting/SSW.VerticalSliceArchitecture/tree/b3926fe461fa79fd81e163d851f1dec00a5ba84e) | xUnit, SQL Server | `GET /api/heroes` | 2 queries, 2 groups |
| [CleanArchitecture](https://github.com/alex289/CleanArchitecture/tree/70a13e310abf8742b938a80dff48ae0735f6b5ef) | NUnit, SQL Server | `GET /api/v1/Tenant/{id}` | 2 queries, 2 groups |

All three request tests passed with no repeated-query finding. The
[validation notes](./docs/case-studies/public-project-validation.md) include the setup, the package
compatibility problem the work found, and the limits of this check.

## Scope and privacy

QueryGuard is focused on query-count regressions.

- It does not prove an N+1.
- It does not observe Dapper or raw ADO.NET.
- It does not collect execution plans or provide a profiler UI.
- It does not rewrite queries or change HTTP responses.
- It is still in preview, so public APIs may change before `1.0.0`.

Parameter values and connection strings are not captured. Redaction runs before any reporter receives
SQL. The JSON report has a `schemaVersion` so format changes are explicit.

## Try the sample

```bash
git clone https://github.com/Benziza/queryguard-dotnet.git
cd queryguard-dotnet
dotnet test samples/QueryGuard.SampleTests
```

The sample includes a 51-query endpoint, a one-query fix, a baseline comparison, and an intentional
repetition with an allowlist reason.

## Documentation

Full documentation and the generated API reference are available at
[benziza.github.io/queryguard-dotnet](https://benziza.github.io/queryguard-dotnet/).

| Guide | Covers |
| --- | --- |
| [How it works](./docs/concepts/README.md) | Sessions, fingerprints, redaction, analysis |
| [Testing](./docs/testing/README.md) | WebApplicationFactory requests and explicit scopes |
| [Public validation](./docs/case-studies/public-project-validation.md) | Results from three public ASP.NET Core projects |
| [Configuration](./docs/configuration/README.md) | Budgets and defaults |
| [Baselines](./docs/baselines/README.md) | Recording and comparing query behavior |
| [Troubleshooting](./docs/troubleshooting/README.md) | Missing capture, grouping, middleware order |
| [False positives](./docs/troubleshooting/false-positives.md) | Allowlisting with a reason |
| [Benchmarks](./docs/benchmarks.md) | Methodology and raw output |
| [Decision records](./docs/decisions/README.md) | Design decisions and tradeoffs |
| [API reference](https://benziza.github.io/queryguard-dotnet/api/) | Every public type |

## Contributing

Issues and focused pull requests are welcome. Small fixes can go straight to a pull request. Open an
issue first for public API, capture, privacy, or detector changes. See [CONTRIBUTING.md](./CONTRIBUTING.md).

False-positive reports are especially useful because they improve the defaults and become regression
fixtures. Report security issues through [SECURITY.md](./SECURITY.md), not a public issue.

## License

MIT. The project takes product inspiration from [Bullet](https://github.com/flyerhzm/bullet) for Rails.
The implementation is independent and uses EF Core's public interception API.
