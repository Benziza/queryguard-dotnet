# How QueryGuard works

Five concepts, in the order a command travels through them.

```text
EF Core executes a command
        │
        ▼
  Interceptor ─────► asks: is a scope open?  ── no ──► do nothing
        │ yes
        ▼
   Fingerprint      normalize → redact → hash
        │
        ▼
    Session         append a record (that is all, per command)
        │
        ▼  scope closes
    Analyzer        group by fingerprint, evaluate the policy
        │
        ▼
   Result           findings, ordered, already redacted
```

## 1. The session is the unit of measurement

A **session** is one HTTP request or one test. It is the thing a query count is a count *of*, and
QueryGuard has nothing useful to say without one.

Sessions come from two places:

- `app.UseQueryGuard()` opens one per request.
- `QueryGuardScope.Start(...)` opens one explicitly, for a test or a background job.

Both nest, and the innermost wins. **No open session means no capture** — QueryGuard stays silent rather
than guessing which scope a command belongs to.

A session is mutable while open and frozen when it completes. `CompletedQueryGuardSession` is a separate
type precisely so "a completed session cannot change" is a compile-time guarantee rather than a
convention.

## 2. The interceptor is stateless

EF Core registers a `DbCommandInterceptor` as a **singleton**. One instance sees commands from every
concurrent request, every parallel test, and every fan-out inside a single request — so it cannot hold
per-scope state. It asks `IQueryGuardSessionAccessor` which session the command it is looking at belongs
to.

The default accessor is backed by `AsyncLocal<T>`, which flows with `ExecutionContext`. That is what makes
`await` boundaries and `Task.Run` fan-out land in the right session without your code passing anything
around.

The limitation is the same mechanism: work that suppresses context flow is not captured. In practice the
one place this bites is `TestServer`, which does not flow context into requests unless asked — see
[troubleshooting](../troubleshooting/README.md#4-testserver-is-not-flowing-executioncontext).

The interceptor **observes**. It never modifies the generated SQL, suppresses a command, changes a result,
or replaces an exception. See [ADR-0002](../decisions/0002-session-propagation.md) and
[ADR-0006](../decisions/0006-aspnet-observe-only.md).

## 3. A fingerprint decides what "the same query" means

To say "this query ran 51 times", QueryGuard has to decide when two command texts are the same query.
Raw text will not do: provider-generated parameter names differ between executions, and formatting
differs between providers and EF versions.

So the command text is **normalized** — whitespace collapsed, non-directive comments removed, every
parameter syntax mapped to one placeholder — then **redacted**, then hashed into a short stable
identifier like `QG-FP-1A2B3C4D`.

Normalization is deliberately conservative. It never reorders tokens, sorts clauses, canonicalizes
aliases, or rewrites quoted identifiers, because the two failure modes are not symmetric:

- **Over-normalizing** merges genuinely different statements, so a report points at SQL your application
  never ran. Actively misleading.
- **Under-normalizing** splits one logical query into several groups, so a real pattern goes unreported.
  The tool is merely quieter.

When in doubt, it does less. See [ADR-0005](../decisions/0005-sql-fingerprints.md).

## 4. Redaction happens once, before anything can read the data

Everything QueryGuard retains passes through one policy. Parameter values and connection strings have no
field anywhere in the model, string and numeric literals surviving in SQL are replaced, retained samples
are bounded, and stack traces are off unless asked for.

Centralizing this is the point: a reporter that had to *remember* to redact would eventually forget, and
adding a reporter would be a way to introduce a leak. Because redaction happens before a result exists,
no reporter — including one you write — can emit what was never captured.

See [ADR-0004](../decisions/0004-parameter-privacy.md).

## 5. Analysis happens after the work, not during it

Capture is one append per command. Everything else — grouping by fingerprint, evaluating budgets,
building findings — happens once, when the scope closes.

That split is why being installed costs about a nanosecond per command
([benchmarks](../benchmarks.md)). It also means analysis can afford to sort and allocate, which is what
makes deterministic ordering affordable: two runs over the same data produce byte-identical reports, so a
snapshot test on a report is meaningful.

A **finding** is evidence, not a verdict on your design. It carries the numbers that justify it —
occurrence counts, expected against actual, timing, redacted SQL — so you can disagree with it on the
facts. A repeated-query candidate is a *warning*, because repeated SQL is strong evidence and not proof.
Making it a failure requires configuring a budget, deliberately.

See [ADR-0003](../decisions/0003-detector-terminology.md).

## Where to go next

| | |
| --- | --- |
| Configure budgets and policies | [configuration](../configuration/README.md) |
| A finding looks wrong | [false positives](../troubleshooting/false-positives.md) |
| Provider support | [providers](../providers/README.md) |
| Why any of this is the way it is | [decision records](../decisions/README.md) |
