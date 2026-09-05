# Configuration

Every budget is opt-in. A policy with nothing configured reports what it sees and fails nothing, which
is what makes QueryGuard safe to add to an existing project: it tells you the truth before it starts
blocking anything.

## Registration

```csharp
builder.Services.AddQueryGuard(options =>
{
    options.Enabled = builder.Environment.IsDevelopment();

    options.DefaultPolicy = QueryGuardPolicy.Create("default")
        .WithMaxQueries(20, QueryGuardSeverity.Warning)
        .WithRepeatedQueryThreshold(3);
});

builder.Services.AddDbContext<AppDbContext>((provider, db) =>
{
    db.UseSqlite(connectionString);
    db.AddInterceptors(provider.GetRequiredService<QueryGuardCommandInterceptor>());
});

var app = builder.Build();

app.UseRouting();
app.UseQueryGuard();   // after UseRouting; see below
```

Two things people miss:

- **Attaching the interceptor is a separate step.** Registering services is not enough; EF Core has to be
  told about it. Outside a container, `options.UseQueryGuard()` does it in one call.
- **`UseQueryGuard()` goes after `UseRouting()`.** The scope name comes from the matched route pattern, so
  earlier means every request lands in one `(unmatched)` scope.

## Budgets

| Method | What it limits | Default severity |
| --- | --- | --- |
| `WithMaxQueries(n)` | Counted commands in the whole scope | Failure |
| `WithMaxOccurrencesPerFingerprint(n)` | Occurrences of any one query | Failure |
| `WithMaxDuplicateGroups(n)` | How many queries reached the repetition threshold | Failure |
| `WithMaxTotalDuration(t)` | Summed command duration | **Warning** |
| `WithSlowQueryThreshold(t)` | Slowest single command | Warning |
| `WithRepeatedQueryThreshold(n)` | When a repetition becomes a candidate warning | Warning (always) |

Every budget is a **maximum**: exactly at the limit passes.

Severity is per rule, and the defaults differ for a reason. Counting rules default to `Failure` because
query count is deterministic: same code, same count. Timing rules default to `Warning` because they are
not, and a guard that fires intermittently on a shared runner teaches people to distrust every other
finding.

**Start with `WithMaxOccurrencesPerFingerprint`.** It is the rule that actually catches an N+1: a
total-count budget can stay satisfied while one query quietly repeats.

```csharp
QueryGuardPolicy.Create("companies")
    .WithMaxQueries(20, QueryGuardSeverity.Warning)      // a canary
    .WithMaxOccurrencesPerFingerprint(5);                 // the actual guard
```

## Per-endpoint policies

A policy is selected by route **pattern**, so `/api/companies/1` and `/api/companies/2` share the policy
for `GET /api/companies/{id}` rather than creating one each.

```csharp
options.ForEndpoint("GET /api/reports/{id}", policy => policy
    .WithMaxQueries(40)
    .WithRepeatedQueryThreshold(6));
```

An override **starts from `DefaultPolicy`**, so a capture setting or allowlist entry added to the default
is not silently lost for every endpoint that has an override.

## What counts as a query

Reader and scalar commands count; writes do not. A budget of ten reads means ten reads regardless of how
many entities the endpoint saves.

```csharp
policy.WithCountedKinds(QueryCommandKind.Reader);   // exclude scalars too
```

Note that "what a command does" is not the same as "which EF Core method executed it". On SQLite, EF Core
runs `INSERT … RETURNING` through the reader path to read the generated key back: QueryGuard classifies
that as a write anyway, or a budget of ten reads would mean something different on every provider.

Only a complete write keyword at the start of a statement marks a reader command as a write.
Strings, quoted identifiers, and comments are skipped when finding statement boundaries. For example,
`SELECT ';DELETE'` counts as one read, while `/* tag */ INSERT ... RETURNING ...` counts as a write.
SQL Server batches with `SET` statements before a write keep the same behavior.

This is a lexical check, not a full SQL parser. CTEs and stored procedure bodies are not interpreted.
If no clear write statement is found, QueryGuard keeps the execution kind supplied by EF Core;
failed commands with no execution kind keep the existing read fallback.

## Capture and privacy

```csharp
options.Capture = new QueryGuardCaptureOptions
{
    CaptureParameterValues = false,   // default; leave it
    CaptureFirstStackTrace = false,   // default; see below
    RedactStringLiterals = true,      // default
    RedactNumericLiterals = true,     // default
    MaxSamplesPerFingerprint = 3,
    MaxNormalizedSqlLength = 4096,
};
```

**`CaptureParameterValues = true` puts real user data into every report QueryGuard produces**, including
any you then attach to a CI artifact or a public issue. It exists because a query executed with 51
*different* keys is stronger evidence than the same query 51 times, but that is a trade to make
deliberately.

**`CaptureFirstStackTrace = true` costs 20–30× the rest of the capture path**: measured, in
[benchmarks](../benchmarks.md). Excellent while hunting a specific repeated query on a development
machine; not something to leave on.

**`RedactNumericLiterals`** has a real trade-off: it also merges queries differing only by a literal such
as `LIMIT 10` versus `LIMIT 100`. An inlined number is more often an identifier than a page size, so the
default treats it as data.

## Documenting intentional repetition

Never reach for a global off switch: there isn't one. Record the exception with its reason, which stays
visible in reports:

```csharp
// Next to the query, when it belongs to one call site.
db.ReportSections.TagWith("QueryGuard:Ignore reason=three-sections-bounded-by-layout")

// Or on the policy, when it belongs to an endpoint.
policy.AllowFingerprint("QG-FP-1A2B3C4D", reason: "Bounded provider lookup; at most three sections.");
```

Full guidance: [false positives](../troubleshooting/false-positives.md).

## Logging

```csharp
options.LogSummaryWhenClean = false;   // default
options.ExcludedRoutePrefixes.Add("/internal");
```

A clean request logs nothing by default. QueryGuard runs on every request, and a clean summary each time
is noise that trains people to filter it out entirely. `/health`, `/healthz`, `/metrics`, and
`/favicon.ico` are excluded out of the box.

Event IDs are stable and documented on `QueryGuardEventIds`: they are part of the observable contract, so
a dashboard can be built on them. `LogLevel.Error` is reserved for QueryGuard's own failures, never for an
application exceeding a budget, so alerting on `Error` stays meaningful.

## Where a repeated query came from

A test scope records the call site of each distinct query by default, so a failure names the code rather
than only the SQL:

```text
  [FAIL] max-occurrences-per-fingerprint: QG-FP-FDB5F469 executed 50 times; the budget is 5.
          SQL: SELECT COUNT(*) FROM "Departments" AS "d" WHERE "d"."CompanyId" = ?
          origin: samples/QueryGuard.SampleApi/Program.cs:line 89
```

One trace per distinct query, never one per execution. Framework and generated frames are filtered out,
so a named method is shown by name and a lambda is shown by file and line: a minimal-API endpoint
compiles to something like `Program.<>c.<<<Main>$>b__0_3>d.MoveNext()`, which carries no information the
location does not.

Turn it off with `captureOrigin: false`, or supply your own redactor to control capture exactly:

```csharp
await using var scope = QueryGuardScope.Start("GET /api/companies", policy, captureOrigin: false);
```

This is on in a scope and **off** on a request path, because it costs 20–30× the rest of the capture
path: free in a test, not free in production. See
[ADR-0007](../decisions/0007-stack-trace-policy.md) and [benchmarks](../benchmarks.md).

## SQL string privacy

String redaction covers single-quoted strings, PostgreSQL `E'...'` escape strings, and
`$$...$$` or `$tag$...$tag$` strings. Quotes and comment markers inside these strings stay
inside the literal during normalization, so their contents are removed before reporting.

An ordinary string containing a backslash before a quote is ambiguous without the database's
SQL mode. QueryGuard hides the remainder of that command in this case. This can shorten the
reported SQL and group more queries together. Use parameterized SQL to avoid this ambiguity.
Setting `RedactStringLiterals = false` explicitly keeps string contents.

## In tests

```csharp
// Where the context is configured.
options.UseSqlite(connectionString).UseQueryGuard();

// In the test.
await using var scope = QueryGuardScope.Start(
    "GET /api/companies",
    QueryGuardPolicy.Create("companies").WithMaxOccurrencesPerFingerprint(1));

var response = await client.GetAsync("/api/companies");

QueryGuardAssert.Passes(await scope.CompleteAsync());
```

`UseQueryGuard()` and `QueryGuardScope.Start` default to the same ambient accessor, so there is nothing
to match up and calling `UseQueryGuard()` twice is a no-op rather than a double count. Pass an accessor
explicitly only when the interceptor came from a container.

With `WebApplicationFactory`, also set `TestServerOptions.PreserveExecutionContext`: see
[troubleshooting](../troubleshooting/README.md#4-testserver-is-not-flowing-executioncontext).
