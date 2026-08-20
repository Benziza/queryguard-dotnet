# Provider support

QueryGuard captures commands through EF Core's relational interception contract, which every relational
provider implements. So in one sense every relational provider works.

But the feature you care about — grouping repeated queries — depends on the *shape of the SQL the provider
generates*. Parameter naming, quoting, and formatting all differ, and the fingerprint normalizer is
deliberately conservative. **Capture and fingerprint quality are two different claims**, and blurring them
is how a support matrix becomes a lie.

## The matrix

| Provider | Tier | What that means |
| --- | --- | --- |
| SQLite | **Integration-tested** | Real commands run in CI, on Ubuntu and Windows, on `net8.0` and `net10.0` |
| PostgreSQL (Npgsql) | **Integration-tested** | Focused Testcontainers suite in CI |
| SQL Server | **Integration-tested** | Real commands run in CI through Testcontainers |
| MySQL / MariaDB | Community | No tests, no promises. Fingerprint quality unverified |
| Other relational providers | Best effort | Works through the official interception contract |
| Non-relational EF providers | **Unsupported** | `DbCommand` interception is relational only |

"Integration-tested" means real database commands run in CI. "Fixture-verified" means the normalizer is
checked against captured SQL from that provider, but nothing live runs. See
[ADR-0009](../decisions/0009-provider-matrix.md).

SQL Server was fixture-verified until a live suite was added, and the first run found a real bug — see
[below](#what-the-live-sql-server-suite-found).

## Why these three, specifically

**SQLite is the workhorse.** Real relational execution, no container, fast enough to run the whole
surface — interception, fingerprinting, budgets, middleware, failure paths — on every pull request.

**PostgreSQL exists to prove the design is not accidentally SQLite-shaped.** Npgsql generates positional
`$1` parameters and quotes differently. With one provider, a dialect assumption could hide in the
normalizer indefinitely; the second data point is what makes the generic approach credible. It also
caught the case that had to be handled explicitly: PostgreSQL's `::` cast operator looks like the start of
a named parameter, and treating it as one silently merged queries that differ by type.

**SQL Server is the provider most .NET developers check first**, so "probably works" was not a good
enough answer for it. It also has the most distinctive generated SQL of the three — a parameter
declaration prologue in front of the actual statement — which turned out to matter more than expected.

## What the live SQL Server suite found

A bug that had shipped, and that fixtures could not have caught.

EF Core's insert batch on SQL Server does not begin with the interesting statement:

```text
SET IMPLICIT_TRANSACTIONS OFF;
SET NOCOUNT ON;
INSERT INTO [Departments] ([Id], [CompanyId], [Name]) VALUES (@p0, @p1, @p2);
```

QueryGuard decides whether a command is a read or a write from its leading keyword, because the
execution method alone is provider-dependent — on SQLite an `INSERT … RETURNING` runs through the
reader path. It saw `SET`, concluded "not a modification", and left the command classified as a read.
So **every `SaveChanges` on SQL Server consumed a read budget**, and a budget of ten reads meant
something different there than on SQLite, quietly.

Classification now walks every statement in the batch rather than only the first. The shapes are pinned
by unit tests that run without Docker, so the regression is caught on every pull request; the live suite
is what noticed it existed.

The general lesson, which is why the tier distinction is kept: a fixture proves the normalizer still
does what it did when the fixture was written. It cannot notice SQL the fixture never contained.

## Parameter syntaxes the normalizer handles

All of these become a single placeholder, which is what lets a per-parent query in a loop group into one
fingerprint instead of N:

| Syntax | Where it comes from |
| --- | --- |
| `@p0`, `@__city_0` | SQL Server, SQLite, MySQL |
| `$1`, `$2` | PostgreSQL positional |
| `:name` | Oracle, some Npgsql configurations |
| `?` | Positional placeholders |

Without this, provider-generated identifiers alone would split one logical query into N groups — precisely
the case QueryGuard exists to find.

## What is *not* normalized

Identifier quoting is left exactly as written: `"Departments"`, `[Departments]`, and `` `Departments` ``
are three different fingerprints. That is deliberate. A table name is structure rather than data, and
rewriting it would mean the report shows SQL your application never ran.

The practical consequence: **a fingerprint is provider-specific.** An allowlist entry recorded against
SQLite will not match the same logical query on PostgreSQL. Allowlist by *tag* when a project runs more
than one provider.

## Running the provider suite

```bash
# SQLite only — no Docker needed
dotnet test tests/QueryGuard.ProviderTests

# With Docker running, the PostgreSQL and SQL Server tests execute too (about a minute)
docker info && dotnet test tests/QueryGuard.ProviderTests
```

The container-backed tests skip themselves when no Docker daemon is reachable, with a skip reason that
says so. A contributor without Docker gets a green run; CI runs them for real.

That skip is for an unavailable environment, not for a flaky test. A container that starts and then
produces inconsistent results is a bug to diagnose, not to retry away.

## Using an untested provider

It will probably work. Capture uses the official contract, so commands will be recorded and grouped. What
is unverified is whether *your* provider's SQL formatting produces equally good fingerprint grouping.

Two failure modes to watch for, and what each means:

| What you see | What it means |
| --- | --- |
| One logical query appears as several fingerprints | Under-normalization. Real patterns go unreported — the tool is quiet, not wrong |
| Two different queries share a fingerprint | Over-normalization. **Report this.** A report pointing at the wrong SQL is worse than no report |

Either is worth a
[provider report](https://github.com/Benziza/queryguard-dotnet/issues/new?template=provider_report.yml).
A synthetic SQL sample becomes a fixture, which is the cheapest way to widen coverage — and the fastest
route from "best effort" to "fixture-verified" for a provider you depend on.

Use synthetic or fully redacted SQL. Do not paste production schema names into a public issue.

## Widening the matrix

A provider moves to "integration-tested" when there is both a suite and someone willing to maintain it.
Adding providers is the easiest way to feel productive and the fastest way to lose a release — each one is
a container in CI, a set of fixtures, and a source of flakiness — so the bar is a contribution that comes
with its own upkeep, not a request.
