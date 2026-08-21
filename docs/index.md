---
title: Catch N+1 queries before merge
description: Counts the EF Core queries your code actually runs, groups the repeated ones, and turns that into something a test can fail on.
---

# Your endpoint returns 200 OK. It also ran the same query 51 times.

A refactor keeps the response correct, keeps the status `200`, keeps every test green, and turns one
query into fifty. Nothing about the response says so, because tests assert *what came back*, not how
many round trips produced it.

QueryGuard counts the EF Core queries your code actually runs, groups the repeated ones, and turns that
into something a test can fail on.

> [!NOTE]
> It reports repeated-query **candidates**. Repeated SQL is strong evidence of an N+1, not proof. Some
> repetition is correct. Every finding says so, and every intentional exception is recorded with a
> reason instead of hidden.

## Install

```bash
dotnet add package QueryGuard.AspNetCore.Testing
```

This package measures real `WebApplicationFactory` requests and works with any test framework.

## Measure one request

Open a measurement for the application's EF Core context:

```csharp
using var factory = new WebApplicationFactory<Program>();
await using var guard = factory.TrackQueries<Program, AppDbContext>(
    "GET /api/companies",
    QueryGuardPolicy.Create("companies").WithMaxOccurrencesPerFingerprint(5));

var response = await guard.Client.GetAsync("/api/companies");
response.EnsureSuccessStatusCode();

QueryGuardAssert.Passes(await guard.CompleteAsync());
```

The helper attaches QueryGuard, preserves the execution context used by `TestServer`, and prevents the
request middleware from hiding the test scope. Outside a scope nothing is captured. See the
[testing guide](testing/README.md) for service tests, background jobs, and manual scopes.

## What a failure tells you

```text
QueryGuard FAILED: GET /api/companies (policy 'companies')
  51 read queries in 2 distinct queries

  [FAIL] max-occurrences-per-fingerprint: QG-FP-FDB5F469 executed 50 times; the budget is 5.
          SQL: SELECT COUNT(*) FROM "Departments" AS "d" WHERE "d"."CompanyId" = ?
          origin: samples/QueryGuard.SampleApi/Program.cs:line 89
```

The last line is the one that saves time: it points at the code that ran the query, not just the SQL.

## Or skip picking a number entirely

`WithMaxOccurrencesPerFingerprint(5)` needs someone to know that five is right. On an endpoint nobody
has measured, nobody does. So record what it costs today and report what changed:

| Scope | Before | Now | Change |
| --- | --: | --: | --- |
| `GET /api/companies` | 3 | 51 | +48, most-repeated query +48 |
| `GET /api/orders` | 8 | 8 | most-repeated query +7 |
| `GET /api/users` | 4 | 4 | unchanged |
| `GET /api/reports` | 12 | 3 | -9 (improved) |

The `orders` row is the one worth a second look: the read count did not move, but one query is now
running seven more of them, which a total-count budget cannot see.

In CI, without writing plumbing:

```bash
dotnet tool install -g QueryGuard.Cli

queryguard baseline record          # once, then commit the file
queryguard verify --summary artifacts/queryguard/summary.md
```

```yaml
- uses: Benziza/queryguard-dotnet@v0.1.0
```

The action posts that table as a sticky pull request comment. See [baselines](baselines/README.md).

## Where to go next

| | |
| --- | --- |
| [How it works](concepts/README.md) | Sessions, fingerprints, redaction, analysis |
| [Testing](testing/README.md) | WebApplicationFactory requests and explicit scopes |
| [Configuration](configuration/README.md) | Every budget and option, and why each default is what it is |
| [Baselines](baselines/README.md) | Recording what a scope costs and reporting what changed |
| [Provider support](providers/README.md) | What is integration-tested and what is merely captured |
| [Troubleshooting](troubleshooting/README.md) | Nothing recorded, fingerprints not grouping, middleware ordering |
| [When a finding is wrong](troubleshooting/false-positives.md) | The allowlist workflow, end to end |
| [Decision records](decisions/README.md) | Why it behaves the way it does |
| [API reference](api/index.md) | Generated from the source |

## Scope

**Tested against real SQL Server, PostgreSQL, MySQL, and SQLite** in CI, on EF Core 8 and 10. Any
relational EF Core provider is captured through the same official interception contract.

It does not prove an N+1, does not see Dapper or raw ADO.NET, produces no execution plans or profiler
UI, and will not fix your code. Profilers and APM answer *what is slow in production?* QueryGuard
answers *did this change alter how many queries we run?* before merge, as a build failure.

It reads SQL, so the defaults are the contract: no parameter values, no connection strings, nothing
written into HTTP responses. Redaction runs centrally before any reporter sees a string, so no
reporter, including one you write, can leak what was never captured.
