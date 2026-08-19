# ADR-0002: Stateless interceptor with an AsyncLocal session accessor

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-009, QG-010, QG-011, R-002

## Context

EF Core registers a `DbCommandInterceptor` as a **singleton** for the lifetime of the
`DbContext` options. A single interceptor instance therefore observes commands from every
concurrent request, every parallel test, and every fan-out inside one request.

QueryGuard needs the opposite: commands grouped by the *scope* that caused them — one HTTP
request, or one integration test. Getting this wrong is not a cosmetic bug. If a command
leaks from one scope into another, every downstream number is wrong: query counts,
fingerprint groups, budget verdicts. And it fails intermittently, which is the worst
possible failure mode for a tool whose entire purpose is to make test results trustworthy.

Options considered:

1. Store the current scope's state in fields on the interceptor.
2. Use `DiagnosticListener` / `EventSource` instead of an interceptor.
3. Pass the session explicitly through the call chain.
4. Keep the interceptor stateless and resolve the session from an ambient accessor.

## Decision

**The interceptor holds no per-scope state. All state lives in a `QueryGuardSession`
resolved through `IQueryGuardSessionAccessor`, whose default implementation is backed by
`AsyncLocal<T>`.**

Concretely:

- The interceptor is safe to share. Its only fields are immutable collaborators
  (the fingerprint service, the options, the clock).
- Opening a scope pushes a session onto the accessor; disposing it restores the previous
  one, so nested scopes work and unwinding is correct even on the exception path.
- The session's accumulator is internally synchronized, because a single request can have
  several EF operations completing concurrently.
- No active session means **no capture**. QueryGuard is silent by default rather than
  guessing which scope a command belongs to.

## Rejected alternatives

**State on the interceptor.** Simplest to write and wrong under any concurrency. It would
work in a single-threaded test and corrupt data in production-shaped code. Rejected outright.

**`DiagnosticListener` only.** It is a legitimate observation channel, but interceptors are
the documented, supported extension point for relational command execution, with explicit
sync/async method pairs and access to the `DbCommand`. Choosing the diagnostic pipeline would
mean reimplementing correlation that the interceptor API already gives us.

**Explicit session passing.** The cleanest model in theory: no ambient state, no
`ExecutionContext` questions. It is unusable in practice, because it would require the
application's own repository and service code to thread a QueryGuard parameter through every
call that might touch the database. A diagnostics tool that forces you to change your
production code to adopt it will not be adopted.

## Consequences

- `AsyncLocal<T>` flows with `ExecutionContext`, which is what makes `await` boundaries and
  `Task.Run` fan-out work. It also means anything that deliberately suppresses flow
  (`ExecutionContext.SuppressFlow`, some custom schedulers, fire-and-forget work started
  before the scope opened) will not be captured. That is a documented limitation, not a bug.
- Isolation is not something we can reason our way into being correct. It requires stress
  tests: many parallel scopes with deliberately different expected counts, repeated across
  iterations, asserting **zero** cross-scope records. These tests are required checks.
- Nested scopes need explicit save/restore semantics, including when the inner scope's body
  throws.
- The accessor is an interface, so a host with a better propagation mechanism can replace it.

## Revisit when

- Evidence appears that `ExecutionContext` propagation is unreliable in a mainstream hosting
  model we claim to support.
- The synchronized accumulator shows up as a measurable cost in the interception benchmark.
  The fix would be a lock-free accumulation strategy, not a weaker isolation guarantee.
