# ADR-0009: SQLite and PostgreSQL are integration-tested; everything else is stated honestly

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-047, QG-048, R-008

## Context

QueryGuard captures commands through EF Core's relational interception contract, which every
relational provider implements. So in one sense every relational provider "works".

But the feature users care about — grouping repeated queries — depends on the *shape of the SQL
the provider generates*. Parameter naming, quoting, declaration blocks, and formatting all differ,
and the fingerprint normalizer is deliberately conservative (see
[ADR-0005](./0005-sql-fingerprints.md)). So capture and fingerprint *quality* are two different
claims, and conflating them is how a support matrix becomes a lie.

There is also a scope trap here. Adding providers is the easiest way to feel productive and the
fastest way to lose a release: each one is a container in CI, a set of fixtures, and a source of
flaky tests, and none of it moves the core product forward.

## Decision

**Three distinct support tiers, stated separately, and never blurred.**

| Provider | Tier | What that means |
| --- | --- | --- |
| SQLite | Integration-tested | Real commands run in CI on Ubuntu and Windows, on both target frameworks |
| PostgreSQL (Npgsql) | Integration-tested | Focused Testcontainers suite in CI |
| SQL Server | Fixture-verified | Captured generated SQL pins the normalizer; no live database in CI |
| MySQL / MariaDB | Community | No tests, no promises |
| Other relational providers | Best effort | Works through the official interception contract |
| Non-relational EF providers | Unsupported | `DbCommand` interception is relational only |

Rules that keep the tiers meaningful:

- SQLite is the workhorse: fast, real relational execution, no container, so it covers
  interception, fingerprinting, budgets, middleware, and failure paths.
- PostgreSQL exists to prove the design against a *second, genuinely different* SQL dialect —
  different parameter syntax, different quoting. One provider would have let dialect assumptions
  hide in the normalizer.
- SQL Server gets fixtures rather than a container because its declaration blocks are the most
  distinctive thing about its SQL, and a fixture pins that at a fraction of the CI cost.
- The PostgreSQL suite skips itself when Docker is unavailable, so a contributor without Docker
  is not blocked. It still runs in CI.
- The README, `SUPPORT.md`, and the package descriptions use these exact words. "Supports all
  EF Core providers" is a banned phrase.

## Rejected alternatives

**Support every provider in v0.1.** Not achievable, and claiming it would be worse than not
trying — the first user on an untested provider would find a bad fingerprint and reasonably
conclude the tool does not work.

**SQLite only.** Cheapest, and it would leave a genuine design risk unexamined: with one dialect,
there is no way to know whether the normalizer generalizes. PostgreSQL is the minimum second
data point.

**A live SQL Server container in CI.** Heavy image, slow startup, licensing considerations, for
marginal additional signal over fixtures at this stage.

## Consequences

- The provider issue form exists so users on untested providers have a real path in, and their
  synthetic SQL becomes a fixture. This is the cheapest possible way to widen coverage.
- CI has to stay fast enough to be usable. The PostgreSQL job runs on provider-relevant changes
  and on `main`, not on every documentation typo.
- Container flakiness is treated as a bug in our test setup, not something to paper over with
  blind retries.

## Revisit when

- A community contribution brings both a provider integration suite and someone willing to
  maintain it.
- Provider support becomes a demonstrated adoption blocker — a user who would use QueryGuard but
  cannot because their provider's fingerprints are wrong. That is worth a provider-specific
  normalizer; a hypothetical user is not.
