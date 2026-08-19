# Support

QueryGuard.NET is maintained by one person in their own time. That shapes what support
looks like: honest expectations, fast triage, and a narrow scope that stays maintainable.

## Where to go

| I want to… | Go here |
| --- | --- |
| Ask how to configure something | [Discussions → Q&A](https://github.com/Benziza/queryguard-dotnet/discussions/categories/q-a) |
| Discuss an API or design idea | [Discussions → Ideas](https://github.com/Benziza/queryguard-dotnet/discussions/categories/ideas) |
| Report unexpected behavior | [Bug report](https://github.com/Benziza/queryguard-dotnet/issues/new?template=bug_report.yml) |
| Report a query QueryGuard wrongly flags | [False-positive report](https://github.com/Benziza/queryguard-dotnet/issues/new?template=false_positive.yml) |
| Report provider-specific behavior | [Provider report](https://github.com/Benziza/queryguard-dotnet/issues/new?template=provider_report.yml) |
| Propose a capability | [Feature request](https://github.com/Benziza/queryguard-dotnet/issues/new?template=feature_request.yml) |
| Report a vulnerability | [SECURITY.md](./SECURITY.md) — **not** a public issue |

A troubleshooting guide ships with the first preview. Until then, the
[architecture decision records](./docs/decisions/README.md) are the best explanation of why
QueryGuard behaves the way it does.

## Response expectations

These are targets, not guarantees.

| Item | Target first response |
| --- | --- |
| Security report | 72 hours |
| False-positive report | 24 hours |
| Bug report | 48 hours |
| Provider report | 72 hours |
| Feature request | 72 hours |
| Discussion question | best effort |

An issue is not ignored because it is quiet — it is triaged in the open. Every issue gets
a decision (`status:accepted` or `status:declined`) with the reasoning written down, rather
than being left to rot.

## Supported versions

| Component | Supported in v0.1 |
| --- | --- |
| .NET | .NET 8, .NET 10 |
| EF Core | EF Core 8, EF Core 10 |
| ASP.NET Core | Matching .NET 8 / .NET 10 |
| Provider requirement | Any **relational** EF Core provider |
| Integration-tested providers | SQLite, PostgreSQL |
| SQL fixture coverage | SQLite, PostgreSQL, SQL Server |

.NET 9 is intentionally not targeted. See
[docs/decisions/0008-target-frameworks.md](./docs/decisions/0008-target-frameworks.md).

"Integration-tested" means real database commands run against that provider in CI.
"Fixture coverage" means the fingerprint normalizer is verified against captured SQL from
that provider, but no live database runs in CI. Any other relational provider works through
the official EF Core interception contract on a best-effort basis — QueryGuard cannot promise
that every provider's SQL formatting produces equally good fingerprint grouping.

## What is out of scope

Saying no early is part of keeping this project usable:

- **A profiler UI or dashboard.** Use MiniProfiler or an APM. QueryGuard is a test-time and
  development-time guard, not a monitoring product.
- **Dapper and raw ADO.NET.** v0.1 captures EF Core relational commands only.
- **Execution plan analysis.** Different problem, provider-specific.
- **Hosted or SaaS analytics.**
- **Automatic query fixes.** QueryGuard reports evidence and points at remediation
  strategies. It does not rewrite your queries.
- **Perfect N+1 proof.** Repeated SQL is strong evidence, not semantic proof. See
  [docs/decisions/0003-detector-terminology.md](./docs/decisions/0003-detector-terminology.md).

## Preview status

Public APIs and the report schema may change before `1.0.0`. Breaking changes in the
preview line are allowed, and every one of them will appear in
[CHANGELOG.md](./CHANGELOG.md) with migration notes.

If you are considering QueryGuard for something you have to maintain, pin an exact version
and read the changelog before upgrading.
