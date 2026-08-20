# Benchmarks

QueryGuard runs on the database command path, so "what does it cost?" deserves a measured answer rather
than an assurance. This page is that answer, with the environment that produced it and the caveats that
limit it.

## How to read this page

**These are microbenchmarks.** They measure QueryGuard's own code in isolation, with no database, no
network, and no HTTP pipeline. A real EF Core query against a real database takes hundreds of
microseconds to milliseconds; the numbers below are nanoseconds. Do not convert them into a percentage
of request latency — that requires measuring *your* application, and the honest statement is that this
page cannot tell you what QueryGuard costs in production.

What it *can* tell you is the relative cost of QueryGuard's own choices, which is what the design
decisions rest on.

## Environment

Reproduce with:

```bash
dotnet run -c Release --project benchmarks/QueryGuard.Benchmarks -- --filter "*" --job Short
```

| | |
| --- | --- |
| Source commit | [`46ec17e`](https://github.com/Benziza/queryguard-dotnet/commit/46ec17ebb9704694e71eb67f73ead60e5556ecca) |
| Measured on | 2026-08-20 |
| BenchmarkDotNet | 0.15.8 |
| Job | `ShortRun` — 3 warmup, 3 iterations, 1 launch |
| OS | Windows 11 (10.0.26200.9168/25H2) |
| CPU | Intel Core Ultra 5 225F, 3.30 GHz, 10 physical / 10 logical cores |
| SDK | .NET 10.0.302, host .NET 10.0.10, X64 RyuJIT `x86-64-v3` |
| EF Core | Not exercised — see below |
| Provider | None — these are in-process only |

**EF Core is not on the measured path.** The benchmark project references it, but every scenario here
drives QueryGuard's own types directly with pre-generated SQL strings. That is deliberate: including a
real query would measure SQLite, not QueryGuard, and the interesting question is what QueryGuard adds.
The cost of EF Core calling an interceptor at all belongs to EF Core.

**`ShortRun` was used deliberately**, and it matters: three iterations produce wide error margins,
visible in the raw output where `Error` sometimes exceeds the mean. Treat every figure below as an order
of magnitude, not a measurement. A number worth quoting publicly needs a default-job run on a quiet
machine; this page exists to support design decisions, and for that the ratios are enough.

**Raw output is published with the summary**:
[`docs/benchmarks/2026-08-20-46ec17e/`](./benchmarks/2026-08-20-46ec17e/) holds the GitHub-format
tables and CSV exactly as BenchmarkDotNet wrote them, including the full environment header. A summary
table without its raw output is a number nobody can check. CI also runs every benchmark once per pull
request with `--job Dry` and uploads the artifacts — that proves the harness still runs, and nothing
about timing.

## Being installed costs nothing measurable

The most important number here. This is what QueryGuard costs per command when it is registered but no
scope is open — every request outside a measured path, and every request at all when
`Enabled = false`.

| Commands | Mean | Allocated |
| --- | --- | --- |
| 1 | 1.11 ns | 0 B |
| 10 | 7.35 ns | 0 B |
| 100 | 91.81 ns | 0 B |

About a nanosecond per command, zero allocation. That is one `AsyncLocal` read and a null check, which
is the whole cost of the "stateless interceptor plus ambient accessor" design in
[ADR-0002](./decisions/0002-session-propagation.md).

It also supports the claim that installing QueryGuard does not change how an application behaves. If
this row were expensive, that claim would be false.

## Capturing and analysing a scope

| Commands | Record | Record + analyse | Allocated (record + analyse) |
| --- | --- | --- | --- |
| 1 | 271 ns | 375 ns | 2,080 B |
| 10 | 770 ns | 1,359 ns | 4,488 B |
| 100 | 5,586 ns | 7,064 ns | 15,728 B |

Per-command cost falls as the scope grows — roughly 270 ns for a single command, 77 ns each at ten, 56 ns
each at a hundred — because the fixed cost of opening a scope amortises. Analysis adds between a quarter
and three quarters on top depending on scope size, and it happens once when the scope closes rather than
per command.

At a hundred commands the whole scope costs about 7 µs. A hundred real database round trips cost several
orders of magnitude more than that.

## Fingerprinting

Normalize, redact, then hash — the work done once per intercepted command.

| Columns in the statement | Full | Normalize only | Redact only |
| --- | --- | --- | --- |
| 3 | 584 ns | 162 ns | 165 ns |
| 20 | 1,138 ns | 486 ns | 482 ns |
| 200 | 7,199 ns | 3,189 ns | 3,459 ns |

Cost grows with SQL length and no faster, which is what a single-pass scanner should do. A quadratic
pass would put the 200-column row at far more than ten times the 20-column row; it is at about six
times.

Normalization and redaction cost about the same as each other, so neither is a hot spot to attack if
this ever needs optimising. The remainder is hashing, whose fixed cost is a noticeable share of a short
statement and almost nothing on a long one.

The 200-column row is a wide report query, not a typical one. A keyed lookup is the 3-column row.

## Stack-trace capture: why it is off by default

This is the measurement [ADR-0007](./decisions/0007-stack-trace-policy.md) was waiting for.

| Distinct fingerprints | Off (default) | On, first occurrence only | Slower by | More allocation by |
| --- | --- | --- | --- | --- |
| 1 | 722 ns | 15,720 ns | 22× | 18× |
| 10 | 5,337 ns | 153,702 ns | 29× | 26× |

Both scenarios record ten commands per fingerprint, so the *only* difference is one captured and filtered
stack trace per distinct query.

**One trace per fingerprint costs 20–30× the entire rest of the capture path, and allocates roughly 350 KB
across ten fingerprints.** That settles the question: capture stays off by default, and the API
deliberately offers no way to capture a trace per command — at ten commands per fingerprint that would
be another order of magnitude.

It also confirms the feature is worth keeping as an opt-in. When you are actively hunting the source of a
repeated query, 150 µs on a development request is nothing, and knowing the call site is worth far more.

What the numbers do *not* support is a claim that enabling it is cheap. It is not, and the documentation
says so.

## Claims this page does not make

Per [docs/testing-strategy.md](./testing-strategy.md), the following will not appear in QueryGuard's
documentation:

- "Zero overhead" — the no-scope path is close, but "close to zero" and "zero" are different claims.
- "Negligible cost" without the number beside it.
- "Fastest EF Core profiler" — QueryGuard is not a profiler and this is not a comparison.
- Any production latency figure derived from a microbenchmark.

If you measure QueryGuard in a real application and the numbers differ from what you expected, that is
worth an issue. Measurement from a real workload is more valuable than anything on this page.
