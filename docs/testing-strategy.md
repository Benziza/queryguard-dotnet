# Testing strategy

QueryGuard runs on a hot, concurrent, provider-dependent path, and its whole value proposition is
that its output can be trusted. So the test suite has to prove five things before any claim is
made publicly: **behavior**, **isolation**, **privacy**, **report stability**, and **honest
performance**.

## Layers

| Layer | Project | Proves | Required on PR |
| --- | --- | --- | --- |
| Contract / unit | `QueryGuard.Core.Tests` | Models, policy precedence, severity, ordering, lifecycle invariants | Yes |
| Fingerprint approval | `QueryGuard.Core.Tests`, `QueryGuard.ProviderTests` | Normalization and fingerprint IDs do not drift | Yes |
| EF interception | `QueryGuard.EntityFrameworkCore.Tests` | Real relational capture, sync and async, failures, no-scope silence | Yes |
| ASP.NET integration | `QueryGuard.AspNetCore.Tests` | Request lifecycle, route policy resolution, response equivalence | Yes |
| Testing API | `QueryGuard.Testing.Tests` | Scope semantics and assertion message quality | Yes |
| Reporter / schema | `QueryGuard.Reporting.Tests` | JSON schema, JUnit validity, log event IDs, redaction | Yes |
| Concurrency stress | `QueryGuard.Core.Tests`, `QueryGuard.AspNetCore.Tests` | Zero cross-session leakage under parallel load | Yes |
| Provider integration | `QueryGuard.ProviderTests` | Provider-specific SQL and capture behavior | Yes, both providers |
| Performance | `QueryGuard.Benchmarks` | Measured hot-path cost | Smoke only |
| Package consumer smoke | `package-validate` job | The built package really works as a package | Yes |

## Critical scenarios

These are the behaviors that must never regress. Each maps to at least one test.

**Scope**

- No active session: the interceptor captures nothing and application behavior is unchanged.
- Two parallel scopes with different query counts: each result contains only its own records.
- A nested scope completing after an exception: the parent session is restored.

**Interception**

- A synchronous `ToList()`: one reader record with a plausible duration and command kind.
- An asynchronous `ToListAsync()`: an equivalent record, with cancellation honored.
- A failing command: failure evidence is recorded **and** the original exception stays primary,
  with its type and stack intact.
- Scalar and non-query commands are distinguishable from reader commands.

**Fingerprinting**

- The same SQL with different whitespace or comments produces the same fingerprint.
- The same EF query with different generated parameter names produces the same fingerprint, per
  provider fixture.
- A `QueryGuard:Ignore` tag survives comment stripping and marks the finding ignored — visibly,
  with its reason, never silently dropped.
- Fingerprint IDs are identical across runs, processes, and both target frameworks.

**Detection and budgets**

- Two occurrences with a threshold of three: no warning. Three: a candidate warning. Boundaries
  are tested from both sides, because off-by-one here means false positives for everyone.
- Total queries exactly at the limit: pass. One over: the configured severity, with expected and
  actual values in the result.
- A per-fingerprint budget breach identifies the fingerprint, the allowed count, the actual
  count, and a sample.
- The duration budget is disabled by default and does not fire even when duration is measured.
- An endpoint-specific policy wins over the default, in a documented resolution order.
- An allowlisted fingerprint remains visible as ignored, with its reason.

**Privacy**

- Default configuration: no parameter values, no connection strings, anywhere in the result or
  in any reporter's output.
- Stack traces off by default: no capture, no allocation.
- Stack traces enabled: exactly one filtered trace per fingerprint group, not one per command.
- Every reporter's output passes the same redaction assertions — a reporter cannot bypass the
  central policy.

**ASP.NET Core**

- Two concurrent requests to different routes: isolated sessions, policies named by route pattern.
- An endpoint that throws after a query: the session is finalized and logged, and the original
  exception pipeline is intact.
- Response equivalence with QueryGuard enabled and disabled: same status, same body, same headers.

**Reporting**

- JSON output is deterministic and carries `schemaVersion`; ignored and failed findings both appear.
- JUnit XML is valid and renders a meaningful test case and failure in common CI viewers.

**End to end**

- The sample's buggy endpoint fails its repeated-query budget; the fixed endpoint returns an
  equivalent response and passes.

## Rules

**Determinism.** Results are immutable and deterministically ordered so snapshots are meaningful.
A test that depends on collection ordering it did not assert is a latent flake.

**No blind retries on correctness tests.** A flaky isolation test is a real bug in either the
code or the test, and retrying it hides the most expensive class of defect QueryGuard can have.
Retry logic is acceptable only for container startup, never for an assertion.

**Timing tests use generous bounds.** CI machines are noisy. Duration-based tests assert
"a duration was measured and is positive", not "under 50 ms". The duration *budget* feature is
off by default for the same reason.

**Approval fixtures are reviewed, not regenerated.** When a fingerprint fixture changes, the diff
is the review. Blindly accepting new output defeats the point.

**Docker is optional locally, required in CI.** The PostgreSQL suite skips itself when Docker is
unavailable so a contributor is never blocked, and runs for real in CI so coverage is not
optional in practice.

**Both target frameworks are tested, not just built.** The reason for multi-targeting is EF Core
behavior differences, and a compile-only pass would not see them.

## Benchmark honesty rules

Benchmarks exist to answer "what does this cost?", not to produce a marketing number.

| Scenario | Baseline | Variant | Measures |
| --- | --- | --- | --- |
| Interceptor, no active scope | EF query without QueryGuard | Interceptor registered, no session | Cost of merely being installed |
| Capture only | No QueryGuard | Active session, fingerprinting disabled | Cost of recording |
| Capture + fingerprint | Capture only | Full normalization and hashing | Cost of grouping |
| First stack trace | Capture + fingerprint | One filtered trace per fingerprint | Cost of the optional evidence |
| Reporter finalization | Completed result, no output | JSON / JUnit / logger written | Post-scope cost |

Every published result discloses CPU, OS, .NET version, EF Core version, provider, scenario
configuration, sample size, and the source commit, with the raw BenchmarkDotNet artifacts
attached.

Claims that are never made: "zero overhead", "negligible cost", "fastest EF Core profiler", or
any production latency conclusion drawn from a microbenchmark.
