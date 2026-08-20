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
| MySQL | **Integration-tested** | Real commands run in CI through Testcontainers, via Oracle's provider — [see the caveat](#the-mysql-provider-caveat) |
| MariaDB | Community | No tests, no promises. Wire-compatible with MySQL, which is evidence and not verification |
| Other relational providers | Best effort | Works through the official interception contract |
| Non-relational EF providers | **Unsupported** | `DbCommand` interception is relational only |

"Integration-tested" means real database commands run in CI. "Fixture-verified" means the normalizer is
checked against captured SQL from that provider, but nothing live runs. See
[ADR-0009](../decisions/0009-provider-matrix.md).

Every one of those live suites has found something. SQL Server was fixture-verified until a live suite
was added, and the first run found [a shipped bug](#what-the-live-sql-server-suite-found). MySQL found
[a reporting bug affecting every provider](#what-the-live-mysql-suite-found). That is the argument for
the tier distinction, restated twice.

## Why these four, specifically

**SQLite is the workhorse.** Real relational execution, no container, fast enough to run the whole
surface — interception, fingerprinting, budgets, middleware, failure paths — on every pull request.

**PostgreSQL exists to prove the design is not accidentally SQLite-shaped.** Npgsql generates positional
`$1` parameters and quotes differently. With one provider, a dialect assumption could hide in the
normalizer indefinitely; the second data point is what makes the generic approach credible. It also
caught the case that had to be handled explicitly: PostgreSQL's `::` cast operator looks like the start of
a named parameter, and treating it as one silently merged queries that differ by type.

**SQL Server is the provider most .NET developers check first**, so "probably works" was not a good
enough answer for it. It also has the most distinctive generated SQL of the four — a parameter
declaration prologue in front of the actual statement — which turned out to matter more than expected.

**MySQL brings the third quoting style and the inlining case.** Backticks are a distinct third form
after `"` and `[]`, and MySQL inlines some constants the other providers parameterize — so where the
SQL Server suite exercises the parameter path, MySQL exercises literal redaction. Both have to end up
hiding the value, and only running both shows that they do.

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

## The MySQL provider caveat

The suite runs against **`MySql.EntityFrameworkCore`**, Oracle's provider — not Pomelo.

Pomelo is the more widely used of the two by a wide margin, so this is worth stating plainly rather
than leaving in a package file: its latest release is `9.0.0` and there is no EF Core 10 line, while
this project targets EF Core 8 and 10. There was no version of Pomelo the suite could have used.

What is verified, precisely: QueryGuard captures and groups MySQL SQL **as Oracle's provider generates
it**. Since a fingerprint is derived from the SQL text, a Pomelo user's SQL may differ in ways this
suite cannot see. Capture is unaffected — that goes through EF Core's interception contract, which both
providers implement identically.

If you run Pomelo and see either failure mode from [the table below](#using-an-untested-provider), it is
worth reporting even though MySQL reads as integration-tested. When Pomelo ships an EF Core 10 line,
running the same suite against it is a small change.

## What the live MySQL suite found

Not a MySQL bug. A bug in what **every** provider reported.

`TagWith` emits the tag as a line comment, and normalization collapses runs of whitespace — including
the line break that terminated the comment. A recognized `QueryGuard:` directive has to survive that
pass, because it changes behaviour, and it was being kept in the form it arrived in:

```text
--QueryGuard:Ignore reason=bounded-reference-lookup SELECT `c`.`Id`, `c`.`City` FROM `Companies` AS `c`
```

One line, and everything after the `--` is inside the comment. Every reporter prints that text, so the
SQL shown for any tagged query read as entirely commented out — and pasting it into a client ran
nothing. An ignored finding is still reported, with its reason, so this was on a path users see.

A directive is now normalized to a block comment whichever way it was written:

```text
/*QueryGuard:Ignore reason=bounded-reference-lookup*/ SELECT `c`.`Id`, `c`.`City` FROM `Companies` AS `c`
```

The block-comment branch was already correct, and a test named for exactly this concern already covered
it. The line-comment branch had the same intent and the opposite outcome, and the assertions on it
checked that `QueryGuard:Ignore` appeared somewhere in the string — which stayed true throughout. A
substring assertion cannot see a delimiter bug.

Two smaller consequences, both improvements: the same directive written `--` or `/* */` now produces one
fingerprint instead of two, which is right because the delimiter is not part of what the query does; and
the fingerprint id of a tagged query changed, so an allowlist entry keyed on one needs the new value.
Baselines are unaffected — they store counts, not fingerprint ids.

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

# With Docker running, the PostgreSQL, SQL Server, and MySQL tests execute too (a few minutes)
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
