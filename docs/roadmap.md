# Roadmap

This roadmap describes direction, not a schedule. Real usage and false-positive reports decide what
moves next. Open an [issue or discussion](https://github.com/Benziza/queryguard-dotnet/issues/new/choose)
to add evidence.

## v0.1

The goal is simple: install QueryGuard, run the sample, and get one actionable repeated-query finding
in under three minutes.

| Area | Shipped scope |
| --- | --- |
| Core | Sessions, immutable records, findings, budgets, allowlists |
| Isolation | `AsyncLocal` scopes, nesting, parallel stress tests |
| Privacy | Central redaction, no parameter values or connection strings |
| EF Core | Sync and async relational command capture on EF Core 8 and 10 |
| Detection | Fingerprint grouping and repeated-query candidate findings |
| ASP.NET Core | DI registration, request middleware, route policies, structured logs |
| Testing | Explicit scopes and assertions with no test framework dependency |
| Reporting | Console, logger, JSON, JUnit, Markdown, SARIF |
| Baselines | Committed JSON baselines, comparison, CLI verification, CI summary action |
| Providers | Live SQLite, PostgreSQL, SQL Server, and MySQL integration suites |
| Packaging | NuGet packages, SourceLink, symbols, package smoke tests, trusted publishing |

## Not planned for v0.1

| Not doing | Why |
| --- | --- |
| Dapper and raw ADO.NET | The first release is limited to one verified capture surface |
| Perfect semantic N+1 proof | SQL repetition is evidence, not enough information for proof |
| Execution plan analysis | It is a different and provider-specific problem |
| Dashboard or hosted UI | QueryGuard is a guard, not a profiler |
| Test framework adapters | The current assertions already work with common frameworks |
| Every EF Core provider | Fingerprint quality must be verified provider by provider |
| Automatic query fixes | QueryGuard reports evidence and does not rewrite application code |

## Candidates after v0.1

These are ordered by current evidence. They are not commitments.

### Better evidence

Classify repeated queries by parameter-set cardinality without retaining values. This could separate
many identical lookups from many different keys, but the privacy design must be approved before code
is written. Track it in [issue #114](https://github.com/Benziza/queryguard-dotnet/issues/114).

Provider-specific normalizers remain possible when a real provider report shows that the general
normalizer is not enough.

### Easier policy placement

Allow endpoint metadata to define a policy next to the endpoint it protects. The design needs clear
precedence between metadata, route configuration, and defaults. Track it in
[issue #102](https://github.com/Benziza/queryguard-dotnet/issues/102).

### More reporting destinations

SARIF, baselines, the CLI, and the pull request report action are shipped. OpenTelemetry export is the
next plausible integration because it sends results to tools teams already use.

### Adoption evidence

The stable API was checked in three public projects. That work found and fixed one package dependency
problem. The [validation notes](./case-studies/public-project-validation.md) record the results and
limits, and [issue #117](https://github.com/Benziza/queryguard-dotnet/issues/117) is complete.

## How priority is decided

1. Correctness: isolation, capture accuracy, and no application behavior changes.
2. Trust: privacy, false positives, and honest support claims.
3. Activation: anything blocking a first useful result.
4. Retention: anything that makes QueryGuard worth keeping.
5. Reach: new providers and integrations.

A reach feature waits while a correctness or trust issue is open. The issue should say why instead of
going quiet.

## Adoption checkpoint

The project should keep expanding when real use produces strong signals such as:

- successful compatibility checks in public projects
- repeat users across releases
- false-positive or provider feedback from real applications

If people show interest but do not reach a first result, improve the setup and message before adding
detectors. If direct onboarding still produces no useful adoption, stop expanding the surface.
