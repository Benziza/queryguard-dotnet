# Support

QueryGuard.NET is maintained by one person in their own time. Support is public, focused, and best
effort.

## Where to go

| I want to | Go here |
| --- | --- |
| Ask a configuration question | [Discussions: Q&A](https://github.com/Benziza/queryguard-dotnet/discussions/categories/q-a) |
| Discuss an API or design | [Discussions: Ideas](https://github.com/Benziza/queryguard-dotnet/discussions/categories/ideas) |
| Report a bug | [Bug report](https://github.com/Benziza/queryguard-dotnet/issues/new?template=bug_report.yml) |
| Report a false positive | [False-positive report](https://github.com/Benziza/queryguard-dotnet/issues/new?template=false_positive.yml) |
| Report provider behavior | [Provider report](https://github.com/Benziza/queryguard-dotnet/issues/new?template=provider_report.yml) |
| Propose a feature | [Feature request](https://github.com/Benziza/queryguard-dotnet/issues/new?template=feature_request.yml) |
| Report a vulnerability | [SECURITY.md](./SECURITY.md), not a public issue |

Start with the [troubleshooting guide](./docs/troubleshooting/README.md) for missing capture,
fingerprint grouping, middleware order, and common false positives.

## Response targets

These are goals, not guarantees.

| Item | First response target |
| --- | --- |
| Security report | 72 hours |
| False-positive report | 24 hours |
| Bug report | 48 hours |
| Provider report | 72 hours |
| Feature request | 72 hours |
| Discussion | Best effort |

Every issue should receive a public decision or next step. Quiet issues are not silently abandoned.

## Supported versions

| Component | Supported in v0.1 |
| --- | --- |
| .NET | .NET 8 and .NET 10 |
| EF Core | EF Core 8 and EF Core 10 |
| ASP.NET Core | Matching .NET 8 or .NET 10 |
| Provider requirement | Any relational EF Core provider |
| Integration-tested providers | SQLite, PostgreSQL, SQL Server, MySQL |

.NET 9 is intentionally not targeted. See
[ADR-0008](./docs/decisions/0008-target-frameworks.md).

Integration-tested means that real database commands run against the provider in CI. Other relational
providers use the same EF Core interception contract, but their SQL formatting has not been verified.
MySQL is tested with Oracle's provider. See the [provider matrix](./docs/providers/README.md).

## Out of scope

- Profiler UI and hosted dashboards
- Dapper and raw ADO.NET capture
- Execution plan analysis
- Automatic query fixes
- Perfect semantic proof of an N+1

QueryGuard reports evidence and enforces budgets. It does not replace a profiler or APM.

## Preview status

Public APIs and the report schema may change before `1.0.0`. Breaking preview changes are documented
in [CHANGELOG.md](./CHANGELOG.md) with migration notes. Pin an exact version and read the changelog
before upgrading.
