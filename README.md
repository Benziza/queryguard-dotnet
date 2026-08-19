<p align="center">
  <img src="./docs/assets/queryguard-logo.svg" width="112" alt="QueryGuard.NET logo">
</p>

<h1 align="center">QueryGuard.NET</h1>

<p align="center">
  <strong>Fail fast on EF Core query regressions.</strong>
</p>

QueryGuard.NET records Entity Framework Core database commands inside an HTTP request or an
integration test, groups repeated SQL fingerprints, and evaluates explicit query budgets.

It targets the class of performance regression that survives code review: the endpoint still
returns `200 OK`, the response is still correct, and the same query now runs 51 times.

> QueryGuard reports **potential N+1 and repeated-query candidates**.
> Repeated SQL is strong evidence, not perfect proof of an application-level N+1 defect.

## Status

**Not released yet.** This repository is being built in public toward `v0.1.0-preview.1`.
There is no NuGet package to install today.

Follow along:

- the [v0.1 preview milestone](https://github.com/Benziza/queryguard-dotnet/milestones) for scope;
- the [issue backlog](https://github.com/Benziza/queryguard-dotnet/issues) for what is being worked on;
- [Discussions](https://github.com/Benziza/queryguard-dotnet/discussions) for API design feedback.

The most useful thing you can do right now is challenge the design while it is still cheap
to change.

## The problem

Functional tests assert response status and content. They rarely assert database round
trips. So a refactor can stay functionally correct while quietly turning one query into
fifty.

QueryGuard turns database behavior into an executable expectation:

- enforce a maximum query count per endpoint or per test;
- identify repeated SQL fingerprints inside one request or test scope;
- warn or fail when repeated-query budgets are exceeded;
- keep intentional exceptions visible through allowlists that require a reason;
- emit structured logs, JSON, and JUnit XML for CI;
- leave the original EF Core command and the application's exception behavior untouched.

## Planned scope for v0.1

| Package | Responsibility |
| --- | --- |
| `QueryGuard.Core` | Session model, command records, fingerprints, budgets, findings |
| `QueryGuard.EntityFrameworkCore` | EF Core relational command capture via `DbCommandInterceptor` |
| `QueryGuard.AspNetCore` | Request-scoped sessions and per-endpoint policies |
| `QueryGuard.Testing` | Explicit scopes and budget assertions for integration tests |
| `QueryGuard.Reporting` | Console/logger, JSON, and JUnit XML reporters |

Targets **.NET 8** and **.NET 10**. .NET 9 is intentionally skipped.

Explicitly **out of scope** for v0.1: a profiler UI, Dapper and raw ADO.NET, execution plan
analysis, hosted analytics, and automatic query fixes. See [SUPPORT.md](./SUPPORT.md).

## Privacy by default

QueryGuard observes SQL, so safe defaults are part of the product contract rather than a
configuration detail. By default it does **not** capture parameter values or connection
strings, does **not** inject anything into HTTP responses, does **not** collect stack traces,
and bounds how many samples it retains per fingerprint. Redaction is applied centrally
before any reporter writes output.

## Contributing

Issues and focused pull requests are welcome, including before the first release.
Start with [CONTRIBUTING.md](./CONTRIBUTING.md).

- Bugs and compatibility reports: [issue forms](https://github.com/Benziza/queryguard-dotnet/issues/new/choose)
- Questions and design discussion: [Discussions](https://github.com/Benziza/queryguard-dotnet/discussions)
- Security vulnerabilities: [SECURITY.md](./SECURITY.md)

## Inspiration

QueryGuard borrows a product lesson from [Bullet](https://github.com/flyerhzm/bullet) for
Ruby on Rails: make hidden query behavior visible and actionable during development and
tests. The implementation here is independent, written for EF Core on top of the official
[EF Core interception APIs](https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors).

## License

QueryGuard.NET is licensed under the [MIT License](./LICENSE).
