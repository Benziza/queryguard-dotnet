# QueryGuard.Testing

Turn database behaviour into an assertion. Open a
[QueryGuard.NET](https://github.com/Benziza/queryguard-dotnet) scope around the code under test and
check that its query budget held.

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

## No test framework dependency

This package references no test framework. `QueryGuardAssert` throws
`QueryGuardBudgetExceededException`, and every test framework reports an unexpected exception with its
message — so xUnit, NUnit, MSTest, and TUnit all work unchanged, and installing this package does not
drag a framework into your project.

Because there is no framework-native formatting to lean on, the exception message carries the evidence:
the policy, expected against actual, the top repeated fingerprint with its redacted SQL, any ignored
findings, and where to read about false positives. See
[ADR-0010](https://github.com/Benziza/queryguard-dotnet/blob/main/docs/decisions/0010-testing-api.md).

Prefer your own assertions? `CompleteAsync` returns the `QueryGuardResult`; assert on it however you like.

## Preview

Public APIs may change before `1.0.0`. See the
[changelog](https://github.com/Benziza/queryguard-dotnet/blob/main/CHANGELOG.md).
