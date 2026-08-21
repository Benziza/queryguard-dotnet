# QueryGuard samples

Two projects, one point: the same endpoint, once with a hidden N+1 and once without.

| Project | What it is |
| --- | --- |
| `QueryGuard.SampleApi` | A minimal ASP.NET Core API over a synthetic company catalogue, wired up with QueryGuard |
| `QueryGuard.SampleTests` | The same endpoints under `QueryGuard.Testing`, showing the assertions you would write for real |

Everything here is invented. No schema, name, or SQL in this repository comes from a real application.

## See it in 30 seconds

```bash
dotnet run --project samples/QueryGuard.SampleApi
```

Then, in another terminal:

```bash
curl http://localhost:5000/api/companies
```

The response is `200 OK` and the data is correct. The log says something else:

```text
warn: QueryGuard.AspNetCore.QueryGuardMiddleware[1000]
      QueryGuard GET /api/companies -> 200: 51 read queries in 2 groups, 0.9 ms database time, 1 failures, 2 warnings, 0 ignored.
warn: QueryGuard.AspNetCore.QueryGuardMiddleware[1002]
      QueryGuard Failure max-occurrences-per-fingerprint: Fingerprint QG-FP-FDB5F469 executed 50 times; the budget is 5.
        Occurrences: 50 (budget: 5)
        Total database time: 0.8 ms
        First seen at command #2, last at command #51
        SQL: SELECT COUNT(*) FROM "Departments" AS "d" WHERE "d"."CompanyId" = ?
```

Two warnings follow it, trimmed here: the `max-queries` budget (51 against 20) and the
`repeated-query` candidate, which carries the caveat that repeated SQL is evidence rather than proof.
The console logger prefixes every line of a multi-line message, so the real output is noisier than the
excerpt above: [`QueryGuardConsoleReporter`](../src/QueryGuard.Reporting/QueryGuardConsoleReporter.cs)
exists to render the same result for reading, and the tests below print it.

QueryGuard is enabled here only in the `Development` environment, which is the posture recommended for
the first preview. `Properties/launchSettings.json` sets that, so `dotnet run` is enough, but a run
that bypasses the launch profile (`--no-launch-profile`, or a container without
`ASPNETCORE_ENVIRONMENT`) produces correct responses and no QueryGuard output at all. That is the
configuration behaving as designed, not a bug, and it is the first thing to check if the log looks
empty.

Now the same data from the fixed endpoint:

```bash
curl http://localhost:5000/api/companies/projected
```

```text
info: QueryGuard.AspNetCore.QueryGuardMiddleware[1000]
      QueryGuard GET /api/companies/projected -> 200: 1 read queries in 1 groups, 0.1 ms database time, 0 failures, 0 warnings, 0 ignored.
```

Fifty-one queries became one. The response did not change: `Both_endpoints_return_identical_data`
asserts exactly that.

## The endpoints

| Endpoint | What it demonstrates |
| --- | --- |
| `GET /api/companies` | The problem. One child query per parent row, and a `200 OK` that hides it |
| `GET /api/companies/projected` | The fix. Projection lets the database do the counting |
| `GET /api/reports/summary` | An *intentional* repetition, documented with a `QueryGuard:Ignore` tag and reported as ignored rather than hidden |

## The tests

```bash
dotnet test samples/QueryGuard.SampleTests
```

Five tests, written to be read:

- `The_problem_endpoint_returns_200_OK_and_still_breaks_its_query_budget`: asserts that the budget
  failure *does* happen, and prints the failure message a developer would actually see. A passing test
  about a failing budget.
- `The_fixed_endpoint_returns_the_same_data_from_one_query`: the assertion as you would write it for
  real: `QueryGuardAssert.Passes(result)` plus an exact query count.
- `Both_endpoints_return_identical_data`: the fix has to be a fix, not a different answer that happens
  to be cheaper.
- `An_intentional_repetition_is_reported_as_ignored_with_its_reason`.
- `The_json_and_junit_reports_render_the_failing_run`: what a CI job would upload as an artifact.

## Two setup details worth copying

**`app.UseQueryGuard()` goes after `app.UseRouting()`.** The scope name comes from the matched route
pattern, so calling it earlier puts every request into a single unmatched scope. QueryGuard still works;
the reports just lose the one label that makes them useful.

**With `WebApplicationFactory`, set `TestServerOptions.PreserveExecutionContext`.** `TestServer` does not
flow `ExecutionContext` into the request pipeline by default, and QueryGuard finds the active session
through `AsyncLocal`. Without it, a scope opened in a test is invisible to the interceptor running inside
the request: the scope completes with zero commands, and an assertion about query counts fails for a
reason that has nothing to do with query counts. `SampleApiFactory` shows the one-line fix, and explains
why setting `Server.PreserveExecutionContext` *after* `CreateClient()` is too late.

## Where the data lives

Running the API creates `queryguard-sample.db` next to it, seeded with 50 companies and 3 departments
each: deterministically, because the numbers above are quoted in the README and a demo whose numbers
move between runs is a demo nobody trusts. Delete the file to reseed.

The tests use a named shared-cache in-memory database instead, so they leave nothing behind.
