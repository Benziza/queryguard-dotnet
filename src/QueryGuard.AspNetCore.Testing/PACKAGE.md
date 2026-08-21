# QueryGuard.AspNetCore.Testing

Measure EF Core queries from a real `WebApplicationFactory` request with one setup call.

```csharp
await using var guard = factory.TrackQueries<Program, CatalogDbContext>(
    "GET /api/companies",
    QueryGuardPolicy.Create("companies").WithMaxQueries(3));

var response = await guard.Client.GetAsync("/api/companies");
var result = await guard.CompleteAsync();

QueryGuardAssert.Passes(result);
```

The helper preserves `ExecutionContext` in `TestServer`, disables the request middleware for the
measurement, attaches the EF Core interceptor, and uses the accessor from the hosted application.
If the interceptor is already attached, it is not added a second time.

The package does not depend on xUnit, NUnit, MSTest, or TUnit.

Full documentation: <https://benziza.github.io/queryguard-dotnet/>
