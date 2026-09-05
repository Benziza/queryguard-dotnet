# Roadmap

This is a direction, not a release schedule.
Share use cases through [issues or discussions](https://github.com/Benziza/queryguard-dotnet/issues/new/choose).

## v0.1

| Area | Included |
| --- | --- |
| Core | Sessions, query limits, findings, and allowlists |
| EF Core | Sync and async relational capture on EF Core 8 and 10 |
| Privacy | SQL redaction; parameter values disabled by default |
| ASP.NET Core | Request middleware, route policies, and logs |
| Testing | Request measurements, explicit scopes, and assertions |
| Reporting | Console, logs, JSON, JUnit, Markdown, and SARIF |
| Baselines | Saved counts, CLI comparison, and a GitHub Action |
| Providers | Live SQLite, PostgreSQL, SQL Server, and MySQL tests |
| Packages | NuGet packages, symbols, SourceLink, and consumer tests |

## Not planned for v0.1

- Dapper or raw ADO.NET capture.
- Semantic proof of an N+1 defect.
- Execution plans, a hosted dashboard, or automatic query fixes.
- Separate test framework adapters.
- Verified support for every EF Core provider.

## Candidates after v0.1

| Idea | Next step |
| --- | --- |
| Count distinct parameter sets without retaining values | Review privacy design: [#114](https://github.com/Benziza/queryguard-dotnet/issues/114) |
| Provider-specific normalization | Add it when real SQL examples show a need |
| Policies in endpoint metadata | Define precedence: [#102](https://github.com/Benziza/queryguard-dotnet/issues/102) |
| OpenTelemetry export | Assess demand from users |

The API was checked in three public projects. See the
[validation results](./case-studies/public-project-validation.md).

## How priority is decided

1. Correct capture and session isolation.
2. Privacy and useful findings.
3. Easy setup.
4. Improvements for existing users.
5. New providers and integrations.

Usage and bug reports guide further work. If setup blocks users, improve it before adding features.