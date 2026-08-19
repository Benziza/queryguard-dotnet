# Roadmap

This roadmap is a statement of intent, not a schedule. Anything past v0.1 depends on what real
users actually need, and the fastest way to influence it is to
[open an issue or a discussion](https://github.com/Benziza/queryguard-dotnet/issues/new/choose).

## v0.1 — first public preview

The goal is narrow and testable: **a developer can install QueryGuard, run the sample, and see
one actionable repeated-query finding in under three minutes.**

| Area | Scope |
| --- | --- |
| Core | Session lifecycle, immutable command records, findings, results, budgets |
| Isolation | `AsyncLocal` session accessor, nested scopes, thread-safe accumulation, parallel stress tests |
| Privacy | One central redaction policy; parameter values and connection strings never captured |
| EF Core | Sync and async relational command capture on EF Core 8 and 10, including failures |
| Fingerprinting | Conservative normalization, stable short IDs, ignore tag, provider fixtures |
| Detection | Fingerprint grouping and potential N+1 / repeated-query candidate findings |
| Budgets | Total count, per-fingerprint repetition, duplicate groups, total duration, severities, named overrides |
| Control | Allowlists by fingerprint, route, and query tag — each requiring a reason |
| ASP.NET Core | DI registration, request-scoped middleware, route policy resolution, structured summary logging |
| Testing | Explicit scopes and budget assertions with no test framework dependency |
| Reporting | Console/logger, JSON with an explicit schema version, JUnit XML |
| Quality | SQLite and PostgreSQL integration suites, SQL Server fixtures, concurrency stress tests |
| Performance | BenchmarkDotNet scenarios with published methodology and raw artifacts |
| Packaging | SourceLink, symbols, package validation, tag-based trusted publishing |

## Explicitly not in v0.1

Saying no is what keeps the first release shippable. Each of these is a real request that
someone will reasonably make:

| Not doing | Why | Revisit when |
| --- | --- | --- |
| Dapper and raw ADO.NET | Wider integration surface than one release can verify | Two independent users ask |
| Perfect semantic N+1 proof | Not reliably inferable from SQL alone ([ADR-0003](./decisions/0003-detector-terminology.md)) | A real semantic detector exists and passes the false-positive fixtures |
| Execution plan analysis | A different problem, and provider-specific | Possible separate package |
| A dashboard or hosted UI | QueryGuard is a guard, not a profiler ([ADR-0006](./decisions/0006-aspnet-observe-only.md)) | Never in the core package |
| NUnit / MSTest / TUnit adapters | Framework sprawl around an unstable API ([ADR-0010](./decisions/0010-testing-api.md)) | Community contribution once the API settles |
| Distributed trace correlation | Not needed for local and CI use | OpenTelemetry integration is a plausible v0.2 |
| Every EF Core provider | Fingerprint quality is provider-dependent ([ADR-0009](./decisions/0009-provider-matrix.md)) | Provider issues from real users |
| Automatic query fixes | QueryGuard reports evidence; rewriting your query is not its job | Not planned |

## Candidates after v0.1

Ordered by how strongly the design already points at them, not by promise. None of these are
committed.

**Evidence quality.** The most valuable direction, because it is what makes findings
trustworthy. Parameter *cardinality* per fingerprint would distinguish "51 identical lookups"
from "51 different keys" — the single strongest N+1 signal — without retaining any value.
Provider-specific normalizers behind the existing interface, if generic normalization proves
insufficient for a provider people actually use.

**Reporting reach.** SARIF output so findings appear in GitHub code scanning. An OpenTelemetry
exporter so budget results reach existing observability. Both are additive and schema-versioned.

**Budget ergonomics.** Baseline comparison — "this endpoint used 12 queries last release and 31
now" — is more useful than an absolute threshold someone has to guess. It needs a stored
baseline, which is a real design question, not a quick feature.

**Adoption friction.** Endpoint metadata attributes so a policy can live next to the endpoint it
guards. A `dotnet` CLI tool for running a report outside a test host.

## How priority is decided

In order:

1. **Correctness** — isolation, capture accuracy, not altering application behavior.
2. **Trust** — false positives, privacy, honest support statements.
3. **Activation** — anything preventing a new user from reaching a first useful result.
4. **Retention** — anything that makes QueryGuard worth keeping in a project.
5. **Reach** — new providers, frameworks, and integrations.

A feature request that lands in category 5 while a category 1 or 2 issue is open will wait, and
the issue will say so rather than going quiet.

## The 30-day decision

Thirty days after the first preview, the project takes an honest look at whether it is used:

- **Continue** if there is at least one strong adoption signal — three real projects, five
  repeat users, or recurring false-positive and provider feedback that only comes from actual use.
- **Reposition** if attention exists but nobody gets to a first result. That means the message or
  the quickstart is wrong, and no amount of new features fixes it.
- **Stop expanding** if nobody reaches first value even after direct onboarding conversations.
  The correct response to that is not more detectors.

Whatever the outcome, it gets written up publicly.
