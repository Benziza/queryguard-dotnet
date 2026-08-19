# ADR-0007: Optional, filtered, one stack trace per fingerprint — off by default

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-032, QG-051, R-006

## Context

"This query ran 51 times" tells you *what*. It does not tell you *where*. The single most
useful addition to a repeated-query finding is the call site that triggered it — and the only
way to get that from inside a `DbCommandInterceptor` is to capture a stack trace.

Stack trace capture is also one of the most expensive things you can do on a hot path. It
allocates, it walks frames, and on the database command path it would run for every command in
every request. A diagnostics tool that measurably slows the thing it measures is a bad trade,
and it also makes its own duration-based budgets less trustworthy.

There is a privacy dimension too: stack traces contain file paths, which on a developer machine
means local directory structure, and in a build means the CI workspace layout.

## Decision

**Stack trace capture is opt-in, bounded to the first occurrence per fingerprint, and filtered.**

- `CaptureFirstStackTrace` defaults to `false`. With the default configuration, no stack trace
  is captured, and no capture code path allocates.
- When enabled, **one** trace is retained per fingerprint group — the first occurrence. There is
  no configuration that captures a trace per command; that path does not exist in the API.
- Frames belonging to QueryGuard, EF Core, and the BCL are filtered out, so what remains is the
  application code that is actually actionable.
- The cost is **measured**, not asserted: a benchmark compares off / first-only across 1 and 10
  fingerprint groups, and the documentation states the numbers with the hardware and runtime they
  came from. No claim of "negligible overhead" appears anywhere without that data.

## Rejected alternatives

**Capture a trace for every command.** The most complete evidence and the worst overhead. It
would also make QueryGuard's own duration measurements misleading. Rejected so firmly that the
API does not expose it even as an option.

**Never capture stack traces.** Cheapest and safest, and it throws away the highest-value
evidence QueryGuard could offer. "Where is this coming from?" is the first question every user
asks after seeing a finding.

**On by default, first occurrence only.** Tempting — the bounded cost is small in absolute
terms. Rejected because QueryGuard's core promise is that installing it does not change how your
application behaves. A default that adds hot-path allocation undermines that promise for a
feature not everyone needs.

## Consequences

- The default experience gives a fingerprint and evidence but not a call site. The finding
  message therefore mentions the option, so a user who wants the call site knows it exists.
- Frame filtering needs its own tests: filtering too aggressively leaves an empty trace, which
  is worse than no trace at all.
- Path filtering is configurable, because what counts as sensitive differs between a laptop and
  a shared build agent.

## Revisit when

- The benchmark shows first-occurrence capture is unacceptable even when enabled. Then it moves
  to a separate opt-in package rather than shipping a trap in the main one.
- Usage evidence shows nobody enables it. A feature nobody uses is a maintenance cost, and
  removing it is cheaper than keeping it.
