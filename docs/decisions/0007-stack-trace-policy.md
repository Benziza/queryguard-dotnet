# ADR-0007: Optional filtered stack traces, off in an application and on in a test

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-032, QG-051, R-006

## Context

"This query ran 51 times" tells you *what*. It does not tell you *where*. The single most
useful addition to a repeated-query finding is the call site that triggered it, and the only
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
- **Except in a test scope, where it defaults to `true`.** `QueryGuardScope.Start` opts in unless
  told otherwise. The measurement above is about the request path in a running application; a scope
  exists only in a test or a deliberate measurement, where 150 µs is free and the call site is the
  difference between *"this endpoint has a repeated query"* and *"line 87 has a repeated query"*.
  Paying the cost in the one place it is worth paying is the point of having the option at all.
  `captureOrigin: false` turns it off, and a caller who supplies their own redactor gets exactly
  what they configured.
- When enabled, **one** trace is retained per fingerprint group: the first occurrence. There is
  no configuration that captures a trace per command; that path does not exist in the API.
- Frames belonging to QueryGuard, EF Core, and the BCL are filtered out, so what remains is the
  application code that is actually actionable.
- The cost is **measured**, not asserted: a benchmark compares off / first-only across 1 and 10
  fingerprint groups, and the numbers are recorded below with the hardware and runtime they came
  from. No claim of "negligible overhead" appears anywhere without that data.

## Measurement

The numbers this decision was waiting for, from
[docs/benchmarks.md](../benchmarks.md): commit `46ec17e`, `ShortRun`, Intel Core Ultra 5 225F,
.NET 10.0.10. Both rows record ten commands per fingerprint, so the only difference is one captured
and filtered trace per distinct query:

| Distinct fingerprints | Off (default) | On, first occurrence only | Slower by | More allocation by |
| --- | --- | --- | --- | --- |
| 1 | 722 ns | 15,720 ns | 22× | 18× |
| 10 | 5,337 ns | 153,702 ns | 29× | 26× |

One trace per fingerprint costs 20–30× the entire rest of the capture path and allocates roughly
350 KB across ten fingerprints. That is decisive for the default, and it also justifies the absence
of a per-command option: at ten commands per fingerprint, that would be another order of magnitude.

It does not argue for removing the feature. On a development request spent hunting a repeated query,
150 µs is nothing and the call site is worth far more. The decision is about the default, not about
whether the capability should exist.

## Rejected alternatives

**Capture a trace for every command.** The most complete evidence and the worst overhead. It
would also make QueryGuard's own duration measurements misleading. Rejected so firmly that the
API does not expose it even as an option.

**Never capture stack traces.** Cheapest and safest, and it throws away the highest-value
evidence QueryGuard could offer. "Where is this coming from?" is the first question every user
asks after seeing a finding.

**On by default, first occurrence only.** Tempting while the cost was only a guess: bounded per
fingerprint, and surely small in absolute terms. The measurement above says otherwise: 22–29×
the rest of the capture path, and hundreds of kilobytes allocated. Rejected on the data as well
as on principle, since QueryGuard's core promise is that installing it does not change how your
application behaves.

## Consequences

- In an application the default experience gives a fingerprint and evidence but not a call site, and
  the finding message mentions the option so a user who wants it knows it exists. In a test the call
  site is there by default, which is where it is actually wanted.
- Frame filtering needs its own tests: filtering too aggressively leaves an empty trace, which is
  worse than no trace at all. It also has to drop generated frames, not only framework namespaces.
  EF Core runs a compiled query through a dynamic method, so the nearest frame to the interceptor was
  `at lambda_method39(Closure, QueryContext)` until a callable name with no dot in it was treated as
  generated by definition.
- Path filtering is configurable, because what counts as sensitive differs between a laptop and
  a shared build agent.

## Revisit when

- The benchmark shows first-occurrence capture is unacceptable even when enabled. Then it moves
  to a separate opt-in package rather than shipping a trap in the main one.
- Usage evidence shows nobody enables it. A feature nobody uses is a maintenance cost, and
  removing it is cheaper than keeping it.
