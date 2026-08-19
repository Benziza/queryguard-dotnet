# Coding standards

The short version lives in [CONTRIBUTING.md](../CONTRIBUTING.md). This document explains *why*
each rule exists, so that a reviewer can apply judgement instead of quoting a list.

The theme running through all of it: QueryGuard is a **library** that runs on the **database
command hot path** and **reads potentially sensitive data**. Each of those three facts removes
some freedom that an application would have.

## Build configuration

| Rule | Where | Why |
| --- | --- | --- |
| `TreatWarningsAsErrors` | `Directory.Build.props` | A warning that is allowed to persist trains everyone to ignore warnings |
| `Nullable` enabled | `Directory.Build.props` | Consumers rely on annotations; an unannotated public API pushes the ambiguity onto them |
| `GenerateDocumentationFile` | `src/Directory.Build.props` | Missing XML docs on a public member fails the build, because the docs are what the consumer sees in IntelliSense |
| `Deterministic` + SourceLink | `src/Directory.Build.props` | A consumer must be able to verify the published binary against the tagged source |
| `EnablePackageValidation` | `src/Directory.Build.props` | Catches an accidental breaking change to the shipped surface before it ships |
| Central package versions | `Directory.Packages.props` | EF Core versions are pinned per target framework in one place, not per project |

Code **style** is deliberately not enforced by the compiler. `dotnet format
--verify-no-changes` is a separate required check, so a style drift can never mask a
correctness warning, and formatting never becomes a review topic.

## Target frameworks

Multi-target `net8.0` and `net10.0`; do not target `net9.0`. See
[ADR-0008](./decisions/0008-target-frameworks.md).

Write C# that compiles on both. Use a new language or BCL feature because it makes the code
clearer, not because it is new — and never leave a consumer of the older target with a
different behavior. Anything genuinely version-specific is isolated behind conditional
compilation with a comment explaining what differs and why.

## Public API design

The public surface is the hardest thing to change after release, so it gets the most scrutiny.

- **Small and intentional.** If a type does not need to be public, it is not public. The
  reflection anchor in `QueryGuard.Core` is `internal` with `InternalsVisibleTo` for tests,
  precisely so it does not become API.
- **Immutable results.** Completed sessions, results, findings, and records are immutable and
  deterministically ordered. A reporter and an assertion must see the same thing in the same
  order every run, or snapshot tests are worthless.
- **Read-only collections.** `IReadOnlyList<T>`, never `List<T>` or an array. A caller must
  not be able to mutate a finding set.
- **Options and builders, not settable grab bags.** Policies are constructed through a
  fluent builder so an invalid combination can be rejected at construction.
- **Nothing sensitive in the model.** Parameter values and connection strings do not exist as
  fields anywhere in the public model, so no reporter can leak what was never captured.

Any public API change needs the API section of the pull request template filled in, and a
breaking one needs an ADR.

## Async and cancellation

- **Implement both interceptor paths.** EF Core has sync and async method pairs. Implementing
  only one produces a tool that silently misses half of a real application's queries.
- **Never block.** No `.Result`, no `.Wait()`, no `GetAwaiter().GetResult()`. Enforced by
  analyzer, because this is the classic way a library deadlocks its consumer.
- **`ConfigureAwait(false)` everywhere in library code.** Also analyzer-enforced. Library code
  has no business capturing a consumer's synchronization context.
- **Accept and forward `CancellationToken`.** Every async reporter and runner API takes one and
  passes it on. A token that is accepted and ignored is worse than one that is absent.

## Concurrency and state

- **The interceptor is stateless.** EF Core shares one interceptor instance across every
  concurrent request and parallel test. Session state lives in `QueryGuardSession`, reached
  through `IQueryGuardSessionAccessor`. See [ADR-0002](./decisions/0002-session-propagation.md).
- **No static mutable session state.** Not as an optimization, not as a shortcut in a test.
- **The accumulator is synchronized**, because one request can have several EF operations
  completing concurrently.
- **Isolation is proven, not argued.** Changes to scope or accumulation behavior require the
  parallel stress tests to pass repeatedly with zero cross-session records.

## Privacy

See [ADR-0004](./decisions/0004-parameter-privacy.md) for the reasoning. In review:

- Parameter values and connection strings are never captured.
- Redaction is applied by one central policy, before any reporter runs. A reporter cannot
  bypass it, and a new reporter cannot introduce a leak.
- Retained samples per fingerprint are bounded.
- A new captured field needs a privacy review, a redaction test, and a documentation entry.
- Tests, fixtures, samples, and documentation use synthetic schemas and data only.

## Performance

QueryGuard runs per database command, so hot-path work is a product decision.

- Consider allocations and synchronization on the capture path. Prefer doing work at
  *finalization*, after the scope closes, over doing it per command.
- Formatting is lazy. Never build a message that might not be emitted.
- Stack trace capture stays off by default and bounded to one per fingerprint
  ([ADR-0007](./decisions/0007-stack-trace-policy.md)).
- **No performance claim without a benchmark.** Every published number carries hardware, OS,
  runtime, EF Core version, scenario, and the raw BenchmarkDotNet artifacts. "Zero overhead"
  and "negligible cost" are banned phrases.

## Logging

- Structured only: event IDs and message templates, never interpolated strings in an
  `ILogger` call. Analyzer-enforced. QueryGuard may log on every request, so allocating a
  message string that gets filtered out is waste.
- Event IDs are stable and centralized. They are part of the observable contract for anyone
  filtering logs.
- Default output is one summary per scope plus the findings — not a line per query.
- Log levels mean something: `Warning` for a candidate finding, `Error` reserved for
  QueryGuard's own failures, never for an application's budget verdict.

## Exceptions

- **Never hide an application exception.** QueryGuard observes. It must not swallow, wrap,
  replace, or reorder an exception raised by the application or the provider.
- **A reporter failure must not mask an application failure.** If writing a report throws
  while an application exception is in flight, the application exception wins.
- The only place QueryGuard throws by design is the explicit testing API, where failing is the
  requested behavior ([ADR-0010](./decisions/0010-testing-api.md)).
- The middleware never throws on the request path
  ([ADR-0006](./decisions/0006-aspnet-observe-only.md)).

## Dependencies

Production dependencies are limited to the BCL, `Microsoft.EntityFrameworkCore.Relational`, and
`Microsoft.Extensions.*` abstractions. Nothing else without an issue explaining what it buys
and what it costs.

Reasons this matters more for a diagnostics library than for an application: every dependency
is a potential version conflict in a consumer's project, a supply-chain surface, and something
a security-conscious evaluator has to review before installing.

No UI, CLI, or serialization framework dependency in `QueryGuard.Core`.

## Tests

- Tests describe **behavior**, not internal structure. A test that breaks when a private
  method is renamed is a maintenance tax.
- A bug fix includes a test that fails before the fix.
- Snapshot and approval tests pin fingerprint normalization, report schemas, and assertion
  messages — the things that must not change accidentally.
- Zero tolerance for flakiness on correctness tests. A flaky isolation test is not retried, it
  is diagnosed. See [testing strategy](./testing-strategy.md).
