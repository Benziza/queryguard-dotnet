# Configuration

Budgets are opt-in. Without a budget, QueryGuard reports findings but does not fail a test.

## Registration

Register the services, attach the interceptor, and add middleware after routing:

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
app.UseQueryGuard();
```

Registering services alone does not attach the interceptor.
Without a DI container, use `options.UseQueryGuard()` when configuring EF Core.

## Budgets

| Method | What it checks | Default severity |
| --- | --- | --- |
| `WithMaxQueries(n)` | Total counted commands | Failure |
| `WithMaxOccurrencesPerFingerprint(n)` | Count for each query ID | Failure |
| `WithMaxDuplicateGroups(n)` | Number of groups that reach the repetition threshold | Failure |
| `WithMaxTotalDuration(t)` | Sum of command durations | Warning |
| `WithSlowQueryThreshold(t)` | Slowest command | Warning |
| `WithRepeatedQueryThreshold(n)` | Repetition count that triggers a candidate warning | Warning (always) |

Maximum budgets pass at the limit. The repetition threshold warns when the count reaches it.
Timing rules default to warnings because database speed varies between runs.

To limit repeated queries:

```csharp
var policy = QueryGuardPolicy.Create("companies")
    .WithMaxQueries(20, QueryGuardSeverity.Warning)
    .WithMaxOccurrencesPerFingerprint(5);
```

This warns above 20 counted commands and fails if one query runs more than five times.

## Per-endpoint policies

Match the route pattern, not a specific URL. Overrides inherit `DefaultPolicy`:

```csharp
options.ForEndpoint("GET /api/reports/{id}", policy => policy
    .WithMaxQueries(40)
    .WithRepeatedQueryThreshold(6));
```

## What counts as a query

Reader and scalar commands count by default. Writes do not.
To count only reader commands:

```csharp
policy = policy.WithCountedKinds(QueryCommandKind.Reader);
```

QueryGuard also checks SQL for writes because EF Core can execute
`INSERT ... RETURNING` through the reader path.

| SQL | Classification |
| --- | --- |
| `SELECT ';DELETE'` | Read |
| `/* tag */ INSERT ... RETURNING ...` | Write |
| `SET NOCOUNT ON; INSERT ...` | Write |

The check skips strings, quoted identifiers, and comments when finding statement boundaries.
It is not a full SQL parser: CTEs and stored procedure bodies are not interpreted.
Without a clear write statement, the EF Core execution kind is kept.
Failed commands with no execution kind use the read fallback.

## Capture and privacy

Default capture options:

```csharp
options.Capture = new QueryGuardCaptureOptions
{
    CaptureParameterValues = false,
    CaptureFirstStackTrace = false,
    RedactStringLiterals = true,
    RedactNumericLiterals = true,
    MaxSamplesPerFingerprint = 3,
    MaxNormalizedSqlLength = 4096,
};
```

- Keep parameter values disabled to avoid putting user data into reports.
- Connection strings are not captured.
- Number redaction groups queries such as `LIMIT 10` and `LIMIT 100` together.
- Set `MaxSamplesPerFingerprint = 0` to keep counts and timing without samples.
- Stack traces add capture cost. See [benchmarks](../benchmarks.md).

## Documenting intentional repetition

Add a reason next to the query or on its policy:

```csharp
var sections = db.ReportSections.TagWith("QueryGuard:Ignore reason=three-sections-bounded-by-layout");

policy = policy.AllowFingerprint(
    "QG-FP-1A2B3C4D",
    reason: "Bounded provider lookup; at most three sections.");
```

Ignored findings stay visible with their reason.
See [when a finding is wrong](../troubleshooting/false-positives.md).

## Logging

```csharp
options.LogSummaryWhenClean = false; // default
options.ExcludedRoutePrefixes.Add("/internal");
```

Clean requests do not log by default. These paths are excluded:
`/health`, `/healthz`, `/metrics`, and `/favicon.ico`.

`QueryGuardEventIds` defines stable log event IDs.
`LogLevel.Error` is used for QueryGuard errors, not exceeded budgets.

## Where a repeated query came from

Test scopes capture one filtered stack trace per query ID by default.
Reports can show the application method or file and line:

```text
origin: samples/QueryGuard.SampleApi/Program.cs:line 89
```

Disable it for a test scope:

```csharp
await using var scope = QueryGuardScope.Start("GET /api/companies", policy, captureOrigin: false);
```

On the request middleware path, stack traces are off by default.
Enable them with `CaptureFirstStackTrace = true`.
The [measured scenarios](../benchmarks.md#stack-trace-capture-why-it-is-off-by-default) were
20–30 times slower with stack traces.

## SQL string privacy

String redaction covers single-quoted strings, PostgreSQL `E'...'` escape strings,
and `$$...$$` or `$tag$...$tag$` strings.

A backslash before a quote in an ordinary string is ambiguous without the database SQL mode.
QueryGuard hides the rest of that command. This can shorten the SQL and group more queries together.
Use parameterized SQL to avoid this ambiguity.

Setting `RedactStringLiterals = false` keeps string contents in reports.

## SQL display length and fingerprints

With the built-in `QueryGuardRedactor`, `MaxNormalizedSqlLength` limits retained SQL to
4096 characters by default, plus a truncation marker.

The ID uses the **complete normalized, redacted SQL** before truncation.
Changing the display limit does not change the ID. Long queries with different endings
keep separate IDs and budgets, even if their displayed text looks the same.
Queries that differ only in redacted values still share an ID.

After upgrading from truncated hashing, review fingerprint allowlists for long queries.
Also review baseline counts, since previously merged query groups may now be separate.

Custom `IQueryGuardRedactor` implementations return the text that is both hashed and retained.
To hash more text than a custom redactor returns, supply a custom `IQueryFingerprintFactory`.

## In tests

Use [`TrackQueries` for HTTP tests or `QueryGuardScope` for services](../testing/README.md).

`UseQueryGuard()` and `QueryGuardScope.Start(...)` share the default session accessor.
Calling `UseQueryGuard()` twice does not add a second interceptor.
If your interceptor comes from DI, pass that container's accessor to the scope.
