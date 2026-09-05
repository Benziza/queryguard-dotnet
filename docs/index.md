---
title: QueryGuard.NET
description: Count EF Core queries and catch repeated queries in tests.
---

# Catch repeated EF Core queries in tests

QueryGuard counts database queries, groups repeated SQL, and checks query limits.
Use it to catch possible N+1 problems before merging your code.

Works with EF Core 8 and 10. See [provider support](providers/README.md).

## Install

For ASP.NET Core integration tests:

```bash
dotnet add package QueryGuard.AspNetCore.Testing
```

Available on [NuGet](https://www.nuget.org/packages/QueryGuard.AspNetCore.Testing).
For services and background jobs, use [QueryGuard.Testing](https://www.nuget.org/packages/QueryGuard.Testing).

## Check one request

Use your application's `Program` and `AppDbContext` types:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using QueryGuard;
using QueryGuard.AspNetCore.Testing;
using QueryGuard.Testing;

using var factory = new WebApplicationFactory<Program>();
await using var guard = factory.TrackQueries<Program, AppDbContext>(
    "GET /api/companies",
    QueryGuardPolicy.Create("companies").WithMaxOccurrencesPerFingerprint(5));

var response = await guard.Client.GetAsync("/api/companies");
response.EnsureSuccessStatusCode();

QueryGuardAssert.Passes(await guard.CompleteAsync());
```

The test fails if the same query runs more than five times. Always send requests through `guard.Client`.

## Read a failure

```text
QueryGuard FAILED: GET /api/companies (policy 'companies')
  51 read queries in 2 distinct queries

  [FAIL] max-occurrences-per-fingerprint: QG-FP-FDB5F469 executed 50 times; the budget is 5.
          SQL: SELECT COUNT(*) FROM "Departments" AS "d" WHERE "d"."CompanyId" = ?
          origin: samples/QueryGuard.SampleApi/Program.cs:line 89
```

The report shows the repeated query, its count, your limit, and where it ran.
Repeated SQL can be intentional. Review each finding before changing the code.

## Next steps

| Task | Guide |
| --- | --- |
| Measure requests, services, or jobs | [Testing](testing/README.md) |
| Set query limits and capture options | [Configuration](configuration/README.md) |
| Compare query counts with a saved result | [Baselines](baselines/README.md) |
| Fix setup problems | [Troubleshooting](troubleshooting/README.md) |
| Allow intentional repetition | [When a finding is wrong](troubleshooting/false-positives.md) |
| Understand sessions and query IDs | [How it works](concepts/README.md) |
| Browse types and methods | [API reference](api/index.md) |

## Limits and privacy

QueryGuard captures relational EF Core commands. It does not capture Dapper or raw ADO.NET calls,
prove an N+1 defect, or fix queries for you.

By default, reports exclude parameter values and redact SQL string and number values.
Connection strings are not captured. See [privacy settings](configuration/README.md#capture-and-privacy).