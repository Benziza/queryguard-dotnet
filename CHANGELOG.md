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

- **A documentation site** at [benziza.github.io/queryguard-dotnet](https://benziza.github.io/queryguard-dotnet/),
  built with DocFX from the same Markdown the repository already had, plus an API reference generated
  from the XML documentation — roughly 330 pages that were previously only readable as source comments.
  Built on every pull request that touches it with warnings promoted to errors, so a dead link fails the
  pull request that introduced it rather than the deployment after it merged.
- Package metadata points at the site. `PackageProjectUrl` is now the documentation site rather than a
  second link to the repository, which nuget.org already shows separately as the source repository, so a
  consumer gets both instead of the same destination twice.
- **A SARIF reporter**, so findings land in GitHub code scanning: the Security tab, and an annotation
  on the line that ran the query in the viewer CodeQL already uses. `QueryGuardSarifReporter` takes the
  repository root, because only a repository-relative path can be matched against a diff.

  Two things about GitHub specifically, both learned by uploading rather than by reading the schema.
  It rejects an entire SARIF file if any one result has no location — which the schema permits — so a
  finding whose origin was not captured goes to a `fallbackPath`, or is left out and counted in
  `runs[0].properties.findingsWithoutLocation`. And a deterministic CI build embeds `/_/` in place of
  the source root, so a stack trace reads `/_/src/Thing.cs`; those paths are recognised and mapped
  rather than passed through as something GitHub cannot resolve.

  A candidate is a `warning` and never an `error`, whatever the policy severity says about failing the
  build, and an allowlisted finding becomes a SARIF suppression carrying its reason rather than being
  dropped. This repository uploads its own sample report on every pull request.
- `QueryGuardOrigin`, which parses the file and line out of a captured stack trace. A trace is fine for
  printing and useless when a consumer needs the two values separately; it declines rather than guesses
  when a frame has no symbols, because a wrong line number in an annotation is worse than no annotation.
- **MySQL is integration-tested.** A Testcontainers suite runs real commands against MySQL 8.4 in CI,
  covering backtick quoting, parameter placeholders, literal redaction, both write shapes, failures, and
  query tags. It moves MySQL from Community to Integration-tested in
  [ADR-0009](docs/decisions/0009-provider-matrix.md), with one caveat stated wherever the claim appears:
  the suite runs against Oracle's `MySql.EntityFrameworkCore`, because Pomelo — the more widely used
  MySQL provider — has no EF Core 10 release. MariaDB deliberately stays Community.

### Fixed

- **A tagged query reported SQL that was entirely commented out.** `TagWith` emits its tag as a line
  comment, and normalization collapses runs of whitespace including the line break that ended it. A
  recognized `QueryGuard:` directive has to survive that pass, and it was kept in the form it arrived
  in — so the normalized text became `--QueryGuard:Ignore reason=x SELECT ...` on one line, with the
  statement inside the comment. Every reporter prints that text, and an ignored finding is still
  reported with its reason, so this was on a path users see. A directive is now normalized to a block
  comment however it was written, which the block-comment branch was already doing correctly.

  Two consequences. The same directive written `--` or `/* */` now produces one fingerprint rather than
  two, which is right — the delimiter is not part of what the query does. And **fingerprints of tagged
  queries have changed**, so a baseline recorded before this release needs re-recording, and an
  allowlist entry keyed on a tagged query's fingerprint needs updating. Allowlisting by tag is
  unaffected.

  Found by running the new MySQL suite; it was never MySQL-specific.
- `queryguard --version` reported the assembly version, `0.1.0.0`, which every preview shares — a bug
  report quoting it could not say which build it came from. It now reports the informational version,
  `0.1.0-preview.3+62d58ff…`, carrying the prerelease suffix and the commit SourceLink stamped in.

## [0.1.0-preview.3] - 2026-08-20

The CI release. A pull request now gets the query-count table as a comment, a failure names the code that
ran the query, and the baseline workflow no longer needs plumbing written by hand.

### Added

- **`QueryGuard.Cli`, a `dotnet queryguard` tool.** `baseline record` reads the JSON reports a test run
  wrote and records what each scope costs; `verify` compares a later run against it, writes the Markdown
  table, and exits 2 with `--fail-on-regression`. Removes the file handling every project would otherwise
  write into a test by hand.
- `QueryGuardJsonReportReader`, which reads a JSON report back into a baseline entry, and
  `QueryGuardBaselineComparison.CompareEntries`, for measurements that did not come from a live run.
- **A GitHub Action** (`Benziza/queryguard-dotnet/action@main`) that publishes the baseline table to the
  job summary and, on a pull request, to a sticky comment it edits rather than duplicating. A composite
  action running one bash script — no JavaScript bundle, no Docker image. It never fails a build for its
  own reasons, and this repository runs it on its own pull requests.
- **A failure now says where the query came from.** A test scope records the call site of each distinct
  query by default and the assertion message prints it as `origin:`, so a failure names the code rather
  than only the SQL. On by default in a scope and still off on a request path, where it costs 20–30× the
  rest of the capture path. `captureOrigin: false` opts out.

### Fixed

- The baseline Markdown table said "1 scope now run more queries" — the noun was pluralised and the verb
  was not. It is the first line of the pull request comment, which makes it the most read sentence the
  tool produces.
- The documented tool install was `dotnet tool install -g QueryGuard.Cli`, which fails while every
  published version is a prerelease — the first command a reader runs would have reported the package
  did not exist. Every instance now passes `--prerelease`.

## [0.1.0-preview.2] - 2026-08-20

The activation release. One package and one line are now enough to capture a query, SQL Server is
integration-tested rather than assumed, and a baseline can replace a guessed budget.

### Fixed

- **A write was counted as a read on SQL Server.** EF Core prefixes its insert batch with
  `SET IMPLICIT_TRANSACTIONS OFF; SET NOCOUNT ON;`, and command classification tested only the leading
  keyword — so it saw `SET`, concluded the command was not a modification, and left it counted as a
  read. Every `SaveChanges` on SQL Server consumed a read budget, which made a budget of ten reads
  mean something different there than on SQLite. Classification now walks every statement in the
  batch. Present in `0.1.0-preview.1`.
- `QueryGuard.Testing` depended only on `QueryGuard.Core`, so installing it alone gave you the scope
  and the assertions and nothing that could capture a command: a first run recorded zero queries and
  every assertion failed for a reason unrelated to the code under test. It now depends on
  `QueryGuard.EntityFrameworkCore`, and one package is enough.

### Added

- **Baseline comparison.** `QueryGuardBaseline` records what each scope costs today into a committed
  JSON file; `QueryGuardBaselineComparison` reports what changed. Removes the guess a budget requires —
  `3 -> 51 queries` needs no threshold to read. A new scope is not a regression, a scope missing from
  the run is ignored rather than reported as removed, and improvements are reported too. See
  `docs/baselines/README.md` and ADR-0013.
- `QueryGuardBaselineMarkdownReporter`, which renders a comparison as a Markdown table for a pull
  request comment or `$GITHUB_STEP_SUMMARY`. It reports the most-repeated-query delta separately from
  the total, because that one moves when the total does not.
- Live SQL Server integration suite through Testcontainers, moving SQL Server from *fixture-verified*
  to *integration-tested*. It found the classification bug above on its first run.
- `UseQueryGuard()` on `DbContextOptionsBuilder`, so attaching QueryGuard outside a dependency
  injection container is one call instead of constructing an interceptor and matching its session
  accessor by hand. Calling it twice is a no-op rather than a double count.
- `AsyncLocalQueryGuardSessionAccessor.Shared`, the ambient accessor both `UseQueryGuard()` and
  `QueryGuardScope.Start` default to.

### Removed

- `docs/launch/` — the article draft, demo script, and community post drafts. They documented how the
  project would be marketed, which is of no use to anyone evaluating whether to install it, and made
  the repository read as a campaign rather than a tool. Kept as local notes instead.

### Changed

- README rewritten around the shortest path that works: problem, install, four-line usage, then the
  baseline table. Cut by a third, with the ASP.NET Core registration block, the running-app walkthrough,
  the reporter table, and the performance detail moved to the pages that already covered them.
- The pull request template asks three questions (what changed, why, how it was tested) instead of
  presenting forty-five checkboxes. The rigorous version moved to `docs/review-checklist.md` for the
  changes that warrant it. Most issue-form fields are now optional; only what a maintainer cannot act
  without stays required, plus the privacy acknowledgements.
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

[Unreleased]: https://github.com/Benziza/queryguard-dotnet/compare/v0.1.0-preview.3...main
[0.1.0-preview.3]: https://github.com/Benziza/queryguard-dotnet/releases/tag/v0.1.0-preview.3
[0.1.0-preview.2]: https://github.com/Benziza/queryguard-dotnet/releases/tag/v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/Benziza/queryguard-dotnet/releases/tag/v0.1.0-preview.1
