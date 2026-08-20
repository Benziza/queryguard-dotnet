# Changelog

All notable changes to QueryGuard.NET are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the version is below `1.0.0`, breaking changes may appear in a minor or preview
release — every one of them is listed here with migration notes.

Generated GitHub release notes list the merged pull requests. This file is the curated
record: breaking changes, privacy-relevant behavior, and report-schema compatibility.

## [Unreleased]

### Changed

- Test tooling: `Microsoft.NET.Test.Sdk` 18.9.0, `xunit.runner.visualstudio` 4.0.0, and
  `coverlet.collector` 10.0.1. Test-only; no shipped dependency changed.

## [0.1.0-preview.1] - 2026-08-20

First public preview. Published to nuget.org with trusted publishing; packages, symbols, and the
generated release notes are attached to the [`v0.1.0-preview.1`](https://github.com/Benziza/queryguard-dotnet/releases/tag/v0.1.0-preview.1)
release.

### Added

- Launch drafts under `docs/launch/`: the technical article, a demo script, and community posts, each
  written under stated rules about what may and may not be claimed.
- Documentation set: a problem-first README with real verified output, plus concept, configuration,
  provider-support, and troubleshooting guides, and a false-positive guide covering the allowlist
  workflow end to end.
- Release workflow: a tag builds, tests, packs, and verifies before publishing with short-lived
  credentials obtained through OIDC, and a manual run rehearses everything except the push.
- Benchmark suite covering the no-active-scope, capture, capture-plus-analysis, fingerprinting, and
  stack-trace paths, with the measured numbers and raw BenchmarkDotNet output published in
  `docs/benchmarks.md`.
- Package verification that asserts metadata, symbols, and framework coverage, then installs the packed
  packages into a throwaway project and runs code against them.
- Provider test suite covering SQLite and PostgreSQL, and a request-level isolation stress suite for
  the ASP.NET Core middleware. Both run in CI.
- Sample application and demonstration tests: a minimal API with one endpoint that returns `200 OK` while
  executing 51 queries, the same endpoint fixed with projection, and an intentional repetition documented
  with a `QueryGuard:Ignore` tag. The demonstration runs in CI.
- `QueryGuard.Reporting`: console, JSON, and JUnit XML reporters. Output is deterministic so a
  snapshot test on it is meaningful, JSON carries an explicit `schemaVersion`, and ignored findings
  are emitted with their reasons rather than dropped.
- `QueryGuard.Testing`: `QueryGuardScope` for opening an explicit session in an integration test, and
  `QueryGuardAssert` for turning a query budget into an assertion. Takes no test framework dependency,
  so the same package works with xUnit, NUnit, MSTest, or TUnit.
- `QueryGuard.AspNetCore`: `AddQueryGuard` registration, `UseQueryGuard` middleware that opens a
  session per request, per-route-pattern policy resolution, and a structured summary with stable
  event IDs. The middleware observes only — the response, its headers, and the original exception are
  never modified.
- Optional first-occurrence stack trace: off by default, bounded to one filtered trace per
  fingerprint, and framework frames removed so what remains is application code. False-positive
  regression fixtures pin the repeated-query patterns that are not defects.
- Transparent allowlists: `QueryGuardPolicy.AllowFingerprint` and `AllowQueryTag`, each requiring a
  reason, plus the `QueryGuard:Ignore` query tag. A matched finding is reported as ignored with its
  reason rather than removed, and allowlisting one fingerprint never suppresses another or a
  session-wide budget.
- Query budgets: total count, per-fingerprint repetition, duplicate-group count, total database
  duration, and slow-query thresholds, each with configurable severity and each producing a finding
  that carries expected and actual values. Command failures are reported as informational evidence
  beside the original exception. Every budget is opt-in and replaceable through
  `IQueryBudgetEvaluator`.
- Repeated-query detection: `QueryGuardAnalyzer` groups a completed session by fingerprint and
  reports potential N+1 candidates as warnings, with the evidence and the limitation attached to the
  finding. `RuleNames` publishes the rule identifiers that appear in reports.
- Conservative SQL normalization: `ISqlNormalizer` collapses whitespace, removes comments other than
  recognized `QueryGuard:` directives, and maps every provider parameter syntax to one placeholder,
  so equivalent generated SQL shares a fingerprint. Token order is never changed. Provider SQL
  fixtures pin the behavior for SQLite, PostgreSQL, SQL Server, and MySQL.
- `QueryGuard.EntityFrameworkCore`: captures relational command execution through the official
  `DbCommandInterceptor` API on EF Core 8 and 10, covering the synchronous and asynchronous reader,
  scalar, and non-query paths plus command failures. Observes only — the generated SQL, the result,
  and the original exception are never modified.
- `IQueryFingerprintFactory` with a stable SHA-256-derived identifier, and `QueryGuardQueryTag` for
  recognizing `QueryGuard:` directives attached with EF Core `TagWith`.
- Central privacy and redaction policy: `QueryGuardCaptureOptions` defines what may be captured
  and `IQueryGuardRedactor` enforces it before any reporter sees a result. Parameter values and
  connection strings are never captured, literals in SQL are redacted, retained samples and SQL
  length are bounded, and stack traces are off by default.
- Async-safe session propagation: `IQueryGuardSessionAccessor` with an `AsyncLocal`-backed
  default, nested scopes that restore the parent session on both the normal and the exception
  path, and out-of-order disposal detection.
- Core contracts: immutable `QueryRecord`, `QueryFingerprint`, `QueryFingerprintGroup`,
  `QueryFinding`, and `QueryGuardResult`; the `QueryGuardSession` lifecycle with a frozen
  `CompletedQueryGuardSession` snapshot; and the immutable fluent `QueryGuardPolicy`.
- Repository foundation: MIT license, community health files, issue forms, pull request
  template, CODEOWNERS, Dependabot configuration, and categorized release notes.
- Shared build configuration with nullable reference types, warnings as errors,
  deterministic builds, central package version management, and package validation.
- CI matrix building and testing `net8.0` and `net10.0` on Ubuntu and Windows, plus
  formatting verification, CodeQL analysis, and dependency review.

### Fixed

- The release workflow resolved the package version by parsing `Directory.Build.props` with a regex
  containing a variable-length lookbehind, which PCRE rejects. `grep` failed, a `|| true` swallowed the
  failure, and the version silently lost its suffix — resolving `0.1.0` for a tag reading
  `0.1.0-preview.1`. The tag comparison refused to publish. The version now comes from
  `dotnet msbuild -getProperty:PackageVersion`, the same property `dotnet pack` stamps on the package,
  and a dry run validates the version it resolved instead of ignoring it.
- The `QueryGuard.AspNetCore` package README showed `app.UseQueryGuard()` with no `app.UseRouting()`
  before it, which names every scope `(unmatched)` and does so silently. Also documented the shared
  accessor requirement in the `QueryGuard.Testing` README, and separated "capture works on any
  relational provider" from "fingerprint grouping is verified on two of them" in the
  `QueryGuard.EntityFrameworkCore` README.
- The sample API produced no QueryGuard output under `dotnet run`, because it enables QueryGuard only in
  the `Development` environment and had no launch profile to set one. Added
  `Properties/launchSettings.json`, and corrected the query and warning counts quoted in
  `samples/README.md` to what the sample actually logs.

[Unreleased]: https://github.com/Benziza/queryguard-dotnet/compare/v0.1.0-preview.1...main
[0.1.0-preview.1]: https://github.com/Benziza/queryguard-dotnet/releases/tag/v0.1.0-preview.1
