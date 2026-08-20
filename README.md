<p align="center">
  <img src="./docs/assets/queryguard-logo.svg" width="112" alt="QueryGuard.NET logo">
</p>

<h1 align="center">QueryGuard.NET</h1>

<p align="center">
  <strong>Your endpoint returns 200 OK. It also ran the same query 51 times.</strong>
</p>

<p align="center">
  <a href="https://github.com/Benziza/queryguard-dotnet/actions/workflows/ci.yml">
    <img alt="CI" src="https://github.com/Benziza/queryguard-dotnet/actions/workflows/ci.yml/badge.svg">
  </a>
  <a href="https://github.com/Benziza/queryguard-dotnet/actions/workflows/codeql.yml">
    <img alt="CodeQL" src="https://github.com/Benziza/queryguard-dotnet/actions/workflows/codeql.yml/badge.svg">
  </a>
  <a href="./LICENSE">
    <img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-blue.svg">
  </a>
  <a href="https://dotnet.microsoft.com/en-us/platform/support/policy">
    <img alt="Targets .NET 8 and .NET 10" src="https://img.shields.io/badge/targets-net8.0%20%7C%20net10.0-512BD4.svg">
  </a>
</p>

QueryGuard.NET counts the Entity Framework Core queries your code actually runs — inside a request or
inside a test — groups the repeated ones, and fails the build when a budget you set is exceeded.

It exists for the regression that survives code review: the response is correct, the status is `200`,
the tests pass, and one query became fifty.

> QueryGuard reports **repeated-query candidates**. Repeated SQL is strong evidence of an N+1 pattern,
> not proof of one — some repetition is correct. Every finding says so, and every intentional exception
> is recorded with a reason instead of hidden.

## See it in under three minutes

```bash
git clone https://github.com/Benziza/queryguard-dotnet.git
cd queryguard-dotnet
dotnet test samples/QueryGuard.SampleTests
```

Five tests pass. One of them is a passing test *about a failing budget*, and it prints what a developer
would actually see:

```text
QueryGuard FAILED: GET /api/companies (policy 'companies')
  51 read queries in 2 distinct queries, 1.6 ms database time
  1 failures, 1 warnings, 0 ignored

Queries by frequency:
  QG-FP-FDB5F469  x50        0.6 ms  SELECT COUNT(*) FROM "Departments" AS "d" WHERE "d"."CompanyId" = ?
  QG-FP-EBC3AACB  x1         1.0 ms  SELECT "c"."Id", "c"."City", "c"."Name" FROM "Companies" AS "c"

Findings:
  [FAIL] max-occurrences-per-fingerprint: Fingerprint QG-FP-FDB5F469 executed 50 times; the budget is 5.
          Occurrences: 50 (budget: 5)
          Total database time: 0.6 ms
          First seen at command #2, last at command #51
          SQL: SELECT COUNT(*) FROM "Departments" AS "d" WHERE "d"."CompanyId" = ?
  [WARN] repeated-query: Potential N+1 pattern in GET /api/companies: fingerprint QG-FP-FDB5F469 executed 50 times.
          Occurrences: 50 (warning threshold: 3)
          Total database time: 0.6 ms (average 0.01 ms)
          First seen at command #2, last at command #51
          SQL: SELECT COUNT(*) FROM "Departments" AS "d" WHERE "d"."CompanyId" = ?
          Repeated SQL is strong evidence, not proof of an application-level N+1 defect.
          Review eager loading, projection, or batching — or record an allowlist entry with a reason if the repetition is intentional.
```

The endpoint returned `200 OK` with correct data for all 50 companies. It took 51 queries to do it, and
nothing in the response, the status code, or a conventional test would have told you.

Prefer to watch it happen in a running app?

```bash
dotnet run --project samples/QueryGuard.SampleApi
```

```bash
curl http://localhost:5000/api/companies
```

`200 OK`, correct JSON, and this in the log:

```text
warn: QueryGuard.AspNetCore.QueryGuardMiddleware[1000]
      QueryGuard GET /api/companies -> 200: 51 read queries in 2 groups, 0.9 ms database time, 1 failures, 2 warnings, 0 ignored.
```

Then the fixed endpoint, returning byte-identical data:

```bash
curl http://localhost:5000/api/companies/projected
```

```text
info: QueryGuard.AspNetCore.QueryGuardMiddleware[1000]
      QueryGuard GET /api/companies/projected -> 200: 1 read queries in 1 groups, 0.1 ms database time, 0 failures, 0 warnings, 0 ignored.
```

Fifty-one queries became one. A test in the sample asserts both endpoints return identical data, because
a "fix" that returns something cheaper and different is not a fix.

See [`samples/`](./samples/) for the whole walkthrough, including an *intentional* repetition that is
reported as ignored with its reason rather than silently suppressed.

## Install

```bash
dotnet add package QueryGuard.AspNetCore --version 0.1.0-preview.1
dotnet add package QueryGuard.Testing --version 0.1.0-preview.1
dotnet add package QueryGuard.Reporting --version 0.1.0-preview.1
```

`QueryGuard.Core` and `QueryGuard.EntityFrameworkCore` come in as dependencies; reference them directly
only if you are using QueryGuard without ASP.NET Core.

*`0.1.0-preview.1` is the first preview. Until it finishes publishing to nuget.org, the clone-and-run
path above works from source.*

## Use it in ASP.NET Core

```csharp
builder.Services.AddQueryGuard(options =>
{
    // Development and test only, for the first preview. QueryGuard observes the database command
    // path, and enabling that in production should be a deliberate act.
    options.Enabled = builder.Environment.IsDevelopment();

    // A warning, not a failure. A new tool should tell you what it sees before it starts failing
    // anything — that is what makes it safe to add to an existing project.
    options.DefaultPolicy = QueryGuardPolicy.Create("default")
        .WithMaxQueries(20, QueryGuardSeverity.Warning)
        .WithRepeatedQueryThreshold(3);

    // A per-fingerprint budget is the rule that actually catches an N+1: a total-count budget can
    // stay satisfied while one query quietly repeats.
    options.ForEndpoint(
        "GET /api/companies",
        policy => policy.WithMaxOccurrencesPerFingerprint(5, QueryGuardSeverity.Failure));
});

builder.Services.AddDbContext<CatalogDbContext>((provider, options) =>
{
    options.UseSqlite(connectionString);

    // The one line that connects QueryGuard to EF Core.
    options.AddInterceptors(provider.GetRequiredService<QueryGuardCommandInterceptor>());
});

var app = builder.Build();

app.UseRouting();

// After UseRouting, so a scope is named by its matched route pattern rather than by its URL.
app.UseQueryGuard();
```

The middleware **observes**. It does not touch the response body, add headers, or replace the exception
your application threw — there is a test that runs the same request with and without QueryGuard and
compares the bytes.

## Use it in a test

This is where a budget stops being a dashboard and starts being a build failure.

```csharp
await using var scope = QueryGuardScope.Start(
    "GET /api/companies/projected",
    QueryGuardPolicy.Create("companies").WithMaxOccurrencesPerFingerprint(5),

    // Pass the accessor the interceptor was built with. Omit this only when the interceptor also
    // uses QueryGuardScope.DefaultAccessor — otherwise the scope records nothing and every count
    // assertion fails for a reason that has nothing to do with the code under test.
    accessor: factory.SessionAccessor);

var response = await client.GetAsync("/api/companies/projected");

var result = await scope.CompleteAsync();

QueryGuardAssert.Passes(result);
QueryGuardAssert.ExecutedQueryCount(1, result);
```

`QueryGuard.Testing` takes no dependency on any test framework, so it works unchanged with xUnit, NUnit,
MSTest, or TUnit. Failure messages carry the counts, the SQL, and the fingerprint — enough to act on
without opening a profiler.

## In CI

`QueryGuard.Reporting` renders a result three ways:

| Reporter | For |
| --- | --- |
| `QueryGuardConsoleReporter` | A CI log, read by a person with nothing else in front of them |
| `QueryGuardJsonReporter` | A dashboard or a trend, with an explicit `schemaVersion` so parsing it is safe |
| `QueryGuardJUnitReporter` | Almost every CI system natively — a budget failure appears where a failing test does |

A warning never fails the JUnit suite. Turning a repeated-query candidate into a red build by default
is how a tool gets switched off instead of tuned.

## What it does not do

Stated up front, because a tool that hides its limits gets distrusted the first time someone finds one.

- **Repeated SQL is evidence, not proof.** Three lookups bounded by the shape of a report are fine.
  QueryGuard cannot tell that from an N+1; you can, and an allowlist entry records the judgement with a
  reason.
- **EF Core only.** Commands issued through Dapper or raw ADO.NET are invisible to it, because it hooks
  EF Core's official `DbCommandInterceptor`.
- **No execution plans, no profiler UI, no hosted analytics.** It counts queries and groups SQL.
- **It will not fix anything for you.** No automatic `Include`, no rewritten LINQ.
- **Two providers are tested**, SQLite and PostgreSQL. Others very likely work — see
  [provider support](./docs/providers/README.md) for what "likely" is worth.
- **.NET 8 and .NET 10.** .NET 9 is deliberately skipped ([ADR-0008](./docs/decisions/0008-target-frameworks.md)).

## How it compares

**Profilers and APM answer "what is slow in production?"** QueryGuard answers "did this change alter how
many queries we run?" — before merge, as a build failure. Different question, different place in the
lifecycle, and they compose fine: keep your APM.

**MiniProfiler and EF Core's own logging show you queries** while you are looking. QueryGuard asserts a
budget when nobody is looking, which is when the regression actually lands.

**Bullet, for Rails**, is where the product idea comes from: make hidden query behaviour visible during
development and tests. The implementation here is independent, built on EF Core's public interception
API.

## Privacy by default

QueryGuard reads SQL, so its defaults are part of the contract rather than a configuration detail. Out of
the box it does **not** capture parameter values, does **not** capture connection strings, does **not**
collect stack traces, does **not** write anything into HTTP responses, and bounds how many samples it
keeps per fingerprint. Redaction runs centrally, before any reporter sees a string.

Every one of those has a test. See [ADR-0004](./docs/decisions/0004-parameter-privacy.md).

## Performance

Registered with no open scope — every request outside a measured path — costs about **1.1 ns per command
and allocates nothing**. That is one `AsyncLocal` read and a null check.

The full numbers, the raw BenchmarkDotNet output, the hardware, and the reasons not to convert any of it
into a production latency figure are in [docs/benchmarks.md](./docs/benchmarks.md).

## Documentation

- [Concepts](./docs/concepts/README.md) — sessions, fingerprints, budgets, findings, and how they fit together.
- [Configuration](./docs/configuration/README.md) — every option, what it defaults to, and why.
- [Troubleshooting](./docs/troubleshooting/README.md) — nothing recorded, unexpected warnings, fingerprints not grouping, middleware ordering.
- [False positives](./docs/troubleshooting/false-positives.md) — when QueryGuard is wrong, and the allowlist workflow end to end.
- [Provider support](./docs/providers/README.md) — what is tested, what is expected to work, and the difference.
- [Benchmarks](./docs/benchmarks.md) · [Roadmap](./docs/roadmap.md) · [Decision records](./docs/decisions/README.md)

## Contributing

Issues and focused pull requests are welcome. Start with [CONTRIBUTING.md](./CONTRIBUTING.md).

The most valuable report right now is a **false positive**: a repeated query QueryGuard flagged that was
correct. That is the failure mode that decides whether a tool like this is worth keeping.

- Bugs and compatibility: [issue forms](https://github.com/Benziza/queryguard-dotnet/issues/new/choose)
- Design questions: [Discussions](https://github.com/Benziza/queryguard-dotnet/discussions)
- Security: [SECURITY.md](./SECURITY.md)

## License

MIT. See [LICENSE](./LICENSE).
