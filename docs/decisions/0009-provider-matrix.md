# ADR-0009: SQLite, PostgreSQL, SQL Server, and MySQL are integration-tested; everything else is stated honestly

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-047, QG-048, QG-067, R-008

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

**Distinct support tiers, stated separately, and never blurred.**

| Provider | Tier | What that means |
| --- | --- | --- |
| SQLite | Integration-tested | Real commands run in CI on Ubuntu and Windows, on both target frameworks |
| PostgreSQL (Npgsql) | Integration-tested | Focused Testcontainers suite in CI |
| SQL Server | Integration-tested | Real commands run in CI through Testcontainers |
| MySQL | Integration-tested | Real commands run in CI through Testcontainers, via Oracle's provider |
| MariaDB | Community | No tests, no promises |
| Other relational providers | Best effort | Works through the official interception contract |
| Non-relational EF providers | Unsupported | `DbCommand` interception is relational only |

Rules that keep the tiers meaningful:

- SQLite is the workhorse: fast, real relational execution, no container, so it covers
  interception, fingerprinting, budgets, middleware, and failure paths.
- PostgreSQL exists to prove the design against a *second, genuinely different* SQL dialect —
  different parameter syntax, different quoting. One provider would have let dialect assumptions
  hide in the normalizer.
- SQL Server is integration-tested because it is the provider most .NET developers evaluate first,
  and because "probably works" was the weakest claim on this page. Adding it found a real bug on the
  first run — see below.
- MySQL is integration-tested for the third quoting style — backticks — and because it inlines some
  constants the other providers parameterize, which is the only live coverage of literal redaction.
  It runs against Oracle's `MySql.EntityFrameworkCore` and not Pomelo, because Pomelo has no EF Core
  10 line; the tier is still "integration-tested" but the provider is named wherever the claim is made.
- The container-backed suites skip themselves when Docker is unavailable, so a contributor without
  Docker is not blocked. They still run in CI.
- The README, `SUPPORT.md`, and the package descriptions use these exact words. "Supports all
  EF Core providers" is a banned phrase.

## Rejected alternatives

**Support every provider in v0.1.** Not achievable, and claiming it would be worse than not
trying — the first user on an untested provider would find a bad fingerprint and reasonably
conclude the tool does not work.

**SQLite only.** Cheapest, and it would leave a genuine design risk unexamined: with one dialect,
there is no way to know whether the normalizer generalizes. PostgreSQL is the minimum second
data point.

**A live SQL Server container in CI.** ~~Heavy image, slow startup, licensing considerations, for
marginal additional signal over fixtures at this stage.~~

**Reversed.** "Marginal additional signal" was the wrong call, and the first live run proved it
within a minute.

EF Core's SQL Server insert batch does not begin with the statement that matters:

```text
SET IMPLICIT_TRANSACTIONS OFF;
SET NOCOUNT ON;
INSERT INTO [Departments] ([Id], [CompanyId], [Name]) VALUES (@p0, @p1, @p2);
```

Command classification tested only the leading keyword, saw `SET`, and left the command counted as a
**read** — so every `SaveChanges` on SQL Server consumed a read budget, and a budget of ten reads meant
something different on SQL Server than on SQLite. That had shipped in `0.1.0-preview.1`.

The fixtures could not have caught it, and that is the general lesson rather than an accident of this
particular bug: a fixture proves the normalizer still does what it did when the fixture was written. It
cannot notice SQL that the fixture never contained, and nobody captures a fixture for a shape they did
not know existed.

The cost turned out to be smaller than assumed too — the whole provider suite, both containers, runs in
about a minute. The licensing consideration is the developer edition the container image ships with,
which is licensed for development and testing.

## Consequences

- The provider issue form exists so users on untested providers have a real path in, and their
  synthetic SQL becomes a fixture. This is the cheapest possible way to widen coverage.
- CI has to stay fast enough to be usable. The provider job runs both containers in about a minute,
  which is cheap enough to run on every pull request rather than only on provider-relevant changes.
- Container flakiness is treated as a bug in our test setup, not something to paper over with
  blind retries.

## Amendment, 2026-08-20: MySQL moved from Community to Integration-tested

The original "Revisit when" said a provider moves up on a community contribution that arrives with its
own upkeep. That is still the bar for a *request*. MySQL was promoted without waiting for one, for a
reason the earlier entry did not anticipate: the existing suites had no coverage of literal redaction
against a live provider, because SQLite, PostgreSQL, and SQL Server all parameterize the constant in
`Where(c => c.City == "Paris")`. MySQL inlines it. That was a gap in the *test matrix*, not a gap in
provider support, and it is the kind of gap a fixture cannot close.

It paid for itself the same way SQL Server did. The live run surfaced a bug in what every provider
reported: a `QueryGuard:` directive arriving as a line comment survived normalization as `--directive`,
and since normalization collapses the line break that ended the comment, the rest of the reported SQL
sat inside it. `TagWith` always emits a line comment, so this was every tagged query everywhere. The
directive is now normalized to a block comment regardless of how it was written.

Two constraints this amendment does not resolve:

- **Pomelo is the provider most MySQL users actually run**, and it has no EF Core 10 release. The suite
  therefore verifies Oracle's SQL generation. Capture is provider-independent, but fingerprints are
  derived from SQL text, so this is a narrower claim than "MySQL works" and is written as such in
  `docs/providers/README.md`.
- **MariaDB stays Community.** Wire compatibility with MySQL is evidence, not verification, and adding a
  fourth container to buy a claim nobody has asked for is exactly the trade this ADR exists to refuse.

## Revisit when

- A community contribution brings both a provider integration suite and someone willing to
  maintain it.
- Pomelo ships an EF Core 10 line. Running the existing MySQL suite against it as a second
  configuration is a small change and would close the caveat above.
- Provider support becomes a demonstrated adoption blocker — a user who would use QueryGuard but
  cannot because their provider's fingerprints are wrong. That is worth a provider-specific
  normalizer; a hypothetical user is not.
