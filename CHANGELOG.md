# Changelog

All notable changes to QueryGuard.NET are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the version is below `1.0.0`, breaking changes may appear in a minor or preview
release — every one of them is listed here with migration notes.

Generated GitHub release notes list the merged pull requests. This file is the curated
record: breaking changes, privacy-relevant behavior, and report-schema compatibility.

## [Unreleased]

### Added

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

[Unreleased]: https://github.com/Benziza/queryguard-dotnet/commits/main
