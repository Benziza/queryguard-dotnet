# Provider support

QueryGuard captures commands through EF Core's relational interceptor API.
SQL grouping depends on the provider's SQL format, so test coverage varies.

## The matrix

| Provider | Coverage |
| --- | --- |
| SQLite | Live CI tests on Ubuntu and Windows, on .NET 8 and 10 |
| PostgreSQL (Npgsql) | Live CI tests with Testcontainers |
| SQL Server | Live CI tests with Testcontainers |
| MySQL | Live CI tests with Oracle's provider; [see below](#the-mysql-provider-caveat) |
| MariaDB | Community use; no live test suite |
| Other relational providers | Best effort; SQL grouping may need more tests |
| Non-relational EF providers | Unsupported |

**Integration-tested** means real database commands run in CI.
**Fixture-verified** means saved SQL is tested without a live database.
See [support policy](../decisions/0009-provider-matrix.md).

## The MySQL provider caveat

The suite uses `MySql.EntityFrameworkCore` from Oracle.
Pomelo SQL is not covered by this suite. Test your application's queries if you use Pomelo.

## Parameter syntaxes the normalizer handles

These parameter references become one common placeholder:

| Syntax | Common use |
| --- | --- |
| `@p0`, `@__city_0` | SQL Server, SQLite, MySQL |
| `$1`, `$2` | PostgreSQL positional parameters |
| `:name` | Named parameters |
| `?` | Positional parameters |

PostgreSQL `::` casts are preserved.

## What is not normalized

Identifier quoting stays unchanged. `"Departments"`, `[Departments]`, and
`` `Departments` `` can produce different fingerprints.

Fingerprint allowlists can therefore differ between providers.
Use a [query tag](../troubleshooting/false-positives.md#3-allowlist-by-tag) for an exception
that should apply across providers.

## Running the provider suite

```bash
dotnet test tests/QueryGuard.ProviderTests
```

SQLite runs without Docker. With Docker running, the PostgreSQL, SQL Server, and MySQL
tests run too. Container tests skip when Docker is unavailable locally; CI runs them.

## Using an untested provider

Check the SQL shown in reports:

| What you see | Possible cause |
| --- | --- |
| One logical query has several IDs | SQL differences are not normalized |
| Different queries share one ID | Normalization or redaction removed a meaningful difference |

Open a [provider report](https://github.com/Benziza/queryguard-dotnet/issues/new?template=provider_report.yml)
with synthetic or fully redacted SQL.

## What the live SQL Server suite found

SQL Server can put `SET` statements before an insert:

```sql
SET IMPLICIT_TRANSACTIONS OFF;
SET NOCOUNT ON;
INSERT INTO [Departments] ([Id], [CompanyId], [Name]) VALUES (@p0, @p1, @p2);
```

Earlier classification counted this as a read. QueryGuard now checks statement boundaries
throughout the batch. See [query classification](../configuration/README.md#what-counts-as-a-query).

## What the live MySQL suite found

A QueryGuard directive in a line comment could make the reported SQL look commented out
after whitespace normalization. Directives now use block comments:

```sql
/*QueryGuard:Ignore reason=bounded-reference-lookup*/ SELECT `c`.`Id` FROM `Companies` AS `c`
```

Line and block forms of the same directive now share an ID.
Review fingerprint allowlists when upgrading from the old behavior.

## Widening the matrix

A new integration-tested provider needs a live test suite and someone to maintain it.
SQL fixtures are also welcome.