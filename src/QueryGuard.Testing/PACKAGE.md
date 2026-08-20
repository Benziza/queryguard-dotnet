# QueryGuard.Testing

Turn database behaviour into an assertion. Open a
[QueryGuard.NET](https://github.com/Benziza/queryguard-dotnet) scope around the code under test and
check that its query budget held.

Two lines of setup. Attach QueryGuard where the context is configured:

```csharp
options.UseSqlite(connectionString).UseQueryGuard();
```

Then measure:

```csharp
[Fact]
public async Task Companies_endpoint_stays_within_its_query_budget()
{
    await using var scope = QueryGuardScope.Start(
        "GET /api/companies",
        QueryGuardPolicy.Create("companies")
            .WithMaxQueries(3)
            .WithMaxOccurrencesPerFingerprint(1));

    var response = await client.GetAsync("/api/companies");
    response.EnsureSuccessStatusCode();

    QueryGuardAssert.Passes(await scope.CompleteAsync());
}
```

No interceptor to construct and no accessor to match up: `UseQueryGuard()` and `QueryGuardScope.Start`
default to the same ambient accessor, so they are wired to each other. Calling `UseQueryGuard()` twice
is a no-op rather than a double count.

Outside a scope, nothing is captured — so leaving the call in place costs about a nanosecond per
command and no allocation.

## When you do need to pass an accessor

Only when the interceptor came from a dependency injection container, in which case hand the scope that
container's accessor:

```csharp
accessor: services.GetRequiredService<IQueryGuardSessionAccessor>()
```

With `WebApplicationFactory` there is one more thing to know: `TestServer` does not flow
`ExecutionContext` into requests unless asked, and QueryGuard finds the active session through
`AsyncLocal`. See
[troubleshooting](https://benziza.github.io/queryguard-dotnet/troubleshooting/README.html).

## No test framework dependency

This package references no test framework. `QueryGuardAssert` throws
`QueryGuardBudgetExceededException`, and every test framework reports an unexpected exception with its
message — so xUnit, NUnit, MSTest, and TUnit all work unchanged, and installing this package does not
drag a framework into your project.

Because there is no framework-native formatting to lean on, the exception message carries the evidence:
the policy, expected against actual, the top repeated fingerprint with its redacted SQL, any ignored
findings, and where to read about false positives. See
[ADR-0010](https://benziza.github.io/queryguard-dotnet/decisions/0010-testing-api.html).

Prefer your own assertions? `CompleteAsync` returns the `QueryGuardResult`; assert on it however you like.

## Preview

Public APIs may change before `1.0.0`. See the
[changelog](https://github.com/Benziza/queryguard-dotnet/blob/main/CHANGELOG.md).
