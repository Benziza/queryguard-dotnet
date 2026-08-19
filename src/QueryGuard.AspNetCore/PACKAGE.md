# QueryGuard.AspNetCore

Opens a [QueryGuard.NET](https://github.com/Benziza/queryguard-dotnet) session around each ASP.NET Core
request, so repeated EF Core queries can be grouped per endpoint and query budgets evaluated per route.

## Quickstart

```csharp
builder.Services.AddQueryGuard(options =>
{
    // Recommended for the first preview: development and test environments.
    options.Enabled = builder.Environment.IsDevelopment();

    options.DefaultPolicy = QueryGuardPolicy.Create("default")
        .WithMaxQueries(20, QueryGuardSeverity.Warning)
        .WithMaxOccurrencesPerFingerprint(5, QueryGuardSeverity.Failure);
});

builder.Services.AddDbContext<AppDbContext>((services, db) =>
{
    db.UseSqlite(connectionString);
    db.AddInterceptors(services.GetRequiredService<QueryGuardCommandInterceptor>());
});

var app = builder.Build();

app.UseQueryGuard();
```

## The middleware observes and nothing else

It does **not** write to the response body, add headers, or throw on the request path. The response
your application produces and the exception it raises are exactly what a client sees, with QueryGuard
enabled or disabled. Findings go to `ILogger` with stable event IDs.

That is a deliberate constraint, not a missing feature — see
[ADR-0006](https://github.com/Benziza/queryguard-dotnet/blob/main/docs/decisions/0006-aspnet-observe-only.md).

## Policies are per route pattern

A policy is selected by the endpoint's route **pattern**, so `/api/companies/1` and
`/api/companies/2` share the policy for `GET /api/companies/{id}` rather than creating one per
identifier.

```csharp
options.ForEndpoint("GET /api/reports/{id}", policy => policy
    .WithMaxQueries(40)
    .AllowFingerprint("QG-FP-1A2B3C4D", reason: "Bounded provider lookup; at most three sections."));
```

## Preview

Public APIs may change before `1.0.0`. See the
[changelog](https://github.com/Benziza/queryguard-dotnet/blob/main/CHANGELOG.md).
