# QueryGuard.EntityFrameworkCore

Captures Entity Framework Core relational commands for
[QueryGuard.NET](https://github.com/Benziza/queryguard-dotnet), so repeated SQL can be grouped and
query budgets can be enforced.

Capture uses the official
[EF Core interception API](https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors).
QueryGuard **observes only**: it never modifies the generated SQL, suppresses a command, changes a
result, or replaces the exception your application sees.

## Registering the interceptor

```csharp
builder.Services.AddDbContext<AppDbContext>((services, db) =>
{
    db.UseSqlite(connectionString);
    db.AddInterceptors(services.GetRequiredService<QueryGuardCommandInterceptor>());
});
```

In a test, or anywhere without a dependency injection container, one line does the same thing and
cannot be wired to the wrong accessor:

```csharp
options.UseSqlite(connectionString).UseQueryGuard();
```

Nothing is captured unless a QueryGuard scope is open — through `QueryGuard.AspNetCore` middleware for
a request, or `QueryGuard.Testing` for a test. With no active scope the interceptor does no work at
all.

## Privacy defaults

Parameter values and connection strings are never captured, literals in SQL are redacted, and
retained samples are bounded. See
[SECURITY.md](https://github.com/Benziza/queryguard-dotnet/blob/main/SECURITY.md).

## Supported versions

| Target framework | EF Core |
| --- | --- |
| `net8.0` | 8.0.x |
| `net10.0` | 10.0.x |

Any **relational** EF Core provider works through the interception contract, so commands will be
captured and grouped. Whether your provider's SQL formatting produces equally good fingerprint
*grouping* is a separate claim: SQLite and PostgreSQL are integration-tested in CI, SQL Server is
verified against captured SQL fixtures, and everything else is unverified. The
[provider matrix](https://benziza.github.io/queryguard-dotnet/providers/README.html)
keeps those tiers apart rather than blurring them.
