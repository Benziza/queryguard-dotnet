# Troubleshooting

| Problem | Go to |
| --- | --- |
| No commands or findings | [Capture setup](#no-findings-recorded) |
| A finding looks wrong | [Intentional repetition](./false-positives.md) |
| One query has several IDs | [Fingerprint grouping](#fingerprints-that-do-not-group) |
| Scope name is `(unmatched)` | [Middleware order](#middleware-ordering) |
| Timing limits fail sometimes | [Duration budgets](#duration-budgets-firing-intermittently) |
| Commands were dropped | [Scope completion](#the-report-says-commands-were-dropped) |

## No findings recorded

For `WebApplicationFactory` tests, start with
[`TrackQueries` and `guard.Client`](../testing/README.md).
For manual setup, check the following.

### 1. No scope was open

Capture needs an active session. Use `app.UseQueryGuard()` for requests or
`QueryGuardScope.Start(...)` for an explicit scope.

### 2. The interceptor is not attached to the `DbContext`

Register QueryGuard services, then attach the interceptor:

```csharp
builder.Services.AddDbContext<AppDbContext>((provider, db) =>
{
    db.UseSqlite(connectionString);
    db.AddInterceptors(provider.GetRequiredService<QueryGuardCommandInterceptor>());
});
```

### 3. The scope and the interceptor read different accessors

Without DI, these APIs share the default accessor:

```csharp
options.UseSqlite(connectionString).UseQueryGuard();
```

If the interceptor comes from DI, pass the same container's accessor to the scope:

```csharp
await using var scope = QueryGuardScope.Start(
    "GET /api/companies",
    policy,
    accessor: services.GetRequiredService<IQueryGuardSessionAccessor>());
```

### 4. `TestServer` is not flowing `ExecutionContext`

A scope opened in a test needs its execution context to reach the request.
`TrackQueries` handles this. For manual setup, configure it **before creating the client**:

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
    => builder.ConfigureServices(services =>
        services.Configure<TestServerOptions>(options => options.PreserveExecutionContext = true));
```

Changing `Server.PreserveExecutionContext` after `CreateClient()` only affects later clients.

### 5. The middleware is shadowing your scope

Request middleware opens an inner session, which receives the commands instead of your test scope.
Disable it in a host that uses explicit test scopes:

```csharp
services.Configure<QueryGuardOptions>(options => options.Enabled = false);
```

### Still nothing?

- Request capture may be disabled through `QueryGuardOptions.Enabled`.
- The path may be excluded: `/health`, `/healthz`, `/metrics`, and `/favicon.ico` are defaults.
- Clean requests do not log unless `LogSummaryWhenClean` is enabled.
- Writes are recorded but are excluded from repeated-query analysis.

## Fingerprints that do not group

Compare the normalized SQL in the report:

| Cause | What to do |
| --- | --- |
| Different predicates or projections | Separate IDs are expected |
| Different providers or identifier quoting | Separate IDs are expected |
| Literal values differ and redaction is off | Check `RedactNumericLiterals` and `RedactStringLiterals` |
| QueryGuard directives differ | Check tags and ignore reasons |
| Provider SQL varies unexpectedly | Open a [provider report](https://github.com/Benziza/queryguard-dotnet/issues/new?template=provider_report.yml) |

Ordinary comments are removed. QueryGuard directives are preserved.
See [how fingerprints work](../concepts/README.md).

## Middleware ordering

Place QueryGuard after routing so scope names use the matched route:

```csharp
app.UseRouting();
app.UseQueryGuard();
app.MapControllers();
```

Running it before routing produces `(unmatched)` scope names.

## Duration budgets firing intermittently

Database timing varies with load. Timing budgets are opt-in and default to warnings.
Use a controlled environment for strict timing limits.

## The report says commands were dropped

`RecordsDroppedAfterCompletion` means commands finished after the scope closed.
Await all measured work before completing the scope.

## Something else

- Ask in [Discussions](https://github.com/Benziza/queryguard-dotnet/discussions).
- Open a [bug report](https://github.com/Benziza/queryguard-dotnet/issues/new?template=bug_report.yml)
  with a small, synthetic example.
- Report vulnerabilities through [SECURITY.md](https://github.com/Benziza/queryguard-dotnet/blob/main/SECURITY.md).