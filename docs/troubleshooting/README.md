# Troubleshooting

Four problems account for almost every question. They are in order of how often they come up.

| Symptom | Jump to |
| --- | --- |
| QueryGuard reports nothing at all | [No findings recorded](#no-findings-recorded) |
| A finding looks wrong | [False positives](./false-positives.md) |
| One logical query appears as several | [Fingerprints that do not group](#fingerprints-that-do-not-group) |
| Every report says `(unmatched)` | [Middleware ordering](#middleware-ordering) |

## No findings recorded

The single most common report, and it is almost always one of five things.

### 1. No scope was open

QueryGuard captures nothing unless a session is active. That is deliberate — it stays silent rather
than guessing which scope a command belongs to. A scope comes from either:

- `app.UseQueryGuard()`, which opens one per request; or
- `QueryGuardScope.Start(...)`, which opens one explicitly.

With neither, the interceptor does one null check and returns.

### 2. The interceptor is not attached to the `DbContext`

Registering QueryGuard's services is not enough. The interceptor has to be attached where EF Core will
see it:

```csharp
builder.Services.AddDbContext<AppDbContext>((provider, db) =>
{
    db.UseSqlite(connectionString);
    db.AddInterceptors(provider.GetRequiredService<QueryGuardCommandInterceptor>());
});
```

### 3. The scope and the interceptor read different accessors

The simplest fix is not to have two. `UseQueryGuard()` and `QueryGuardScope.Start` both default to the
same ambient accessor, so this cannot happen unless you opt into it:

```csharp
options.UseSqlite(connectionString).UseQueryGuard();
```

It only applies when the interceptor came from a dependency injection container. Then hand the scope
that container's accessor:

```csharp
await using var scope = QueryGuardScope.Start(
    "GET /api/companies",
    policy,
    accessor: services.GetRequiredService<IQueryGuardSessionAccessor>());
```

### 4. `TestServer` is not flowing `ExecutionContext`

**This is the one that wastes the most time.** `TestServer` does not flow `ExecutionContext` into the
request pipeline by default, and QueryGuard finds the active session through `AsyncLocal`. A scope
opened in a test is therefore invisible to the interceptor running inside the request: the scope
completes with zero commands, and an assertion about query counts fails for a reason that has nothing to
do with query counts.

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
    => builder.ConfigureServices(services =>
        services.Configure<TestServerOptions>(options => options.PreserveExecutionContext = true));
```

Setting `Server.PreserveExecutionContext` *after* `CreateClient()` does not work: the flag is captured
when the client's handler is built, so it affects only the next client. That produces the confusing
version of this problem, where some tests capture and some do not depending on execution order.

### 5. The middleware is shadowing your scope

Both the middleware and an explicit scope open sessions, and the innermost one wins. In a test that
opens its own scope against a host with `UseQueryGuard()` active, the middleware's per-request session
shadows yours and your scope sees nothing. Disable it in the test host:

```csharp
services.Configure<QueryGuardOptions>(options => options.Enabled = false);
```

### Still nothing?

- `QueryGuardOptions.Enabled` may be `false` — the sample gates it on `IsDevelopment()`.
- The path may be excluded. `/health`, `/healthz`, `/metrics`, and `/favicon.ico` are ignored by default.
- A clean request logs nothing unless `LogSummaryWhenClean` is set. That is not a bug; QueryGuard runs on
  every request, and a clean summary each time is noise.
- Writes are recorded but never grouped for repeated-query analysis. Fifty inserts produce no findings.

## Fingerprints that do not group

If one logical query appears as several fingerprints, a repeated-query pattern will not be reported —
the tool goes quiet rather than wrong, which makes this failure mode easy to miss.

Normalization is deliberately conservative: it collapses whitespace, removes non-directive comments,
and replaces parameter references, but it never reorders tokens or rewrites identifiers. So two
statements that differ in any other way are genuinely different fingerprints.

Check the normalized SQL in the report. Common causes:

| Cause | What to do |
| --- | --- |
| The queries really are different (different predicate, different projection) | Nothing — this is correct |
| Different providers | Expected. Identifier quoting differs and is not rewritten |
| Inlined literals differ and redaction is off | Leave `RedactNumericLiterals` and `RedactStringLiterals` on |
| A `TagWith` tag differs between call sites | A tagged query is a distinct fingerprint by design |
| Provider SQL varies in a way normalization does not cover | Open a [provider report](https://github.com/Benziza/queryguard-dotnet/issues/new?template=provider_report.yml) with the redacted SQL |

The last row is the useful one. A synthetic SQL sample from a provider becomes a fixture, which is the
cheapest way to widen coverage.

## Middleware ordering

If every report is named `(unmatched)`, `UseQueryGuard()` is running before `UseRouting()`. The scope
name comes from the matched endpoint's route pattern, and before routing there is no endpoint yet.

```csharp
app.UseRouting();
app.UseQueryGuard();   // after routing
app.MapControllers();
```

QueryGuard still works in the wrong order — it just loses the one label that makes a report useful.

## Duration budgets firing intermittently

That is why they are off by default. Database timing varies with machine load, and a shared CI runner
is the worst place to measure it. A duration budget belongs in an environment whose timing you control.
If you need one in CI, set it generously and expect to revisit it.

## The report says commands were dropped

`RecordsDroppedAfterCompletion` means commands finished after the scope closed. Almost always
fire-and-forget work started inside a request. The commands are genuinely outside the measured window,
so they are counted and reported rather than silently added to a scope that had already been reported.

## Something else

- Ask in [Discussions](https://github.com/Benziza/queryguard-dotnet/discussions).
- File a [bug report](https://github.com/Benziza/queryguard-dotnet/issues/new?template=bug_report.yml)
  with a minimal synthetic reproduction.
- For a vulnerability, follow [SECURITY.md](../../SECURITY.md) rather than opening an issue.
