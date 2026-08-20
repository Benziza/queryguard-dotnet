# QueryGuard.Reporting

Render a [QueryGuard.NET](https://github.com/Benziza/queryguard-dotnet) result as console text,
versioned JSON, or JUnit XML.

```csharp
var result = await scope.CompleteAsync();

// Human-readable, for a terminal.
Console.Write(new QueryGuardConsoleReporter().Render(result));

// Machine-readable, with an explicit schema version.
await new QueryGuardJsonReporter().WriteAsync(result, "artifacts/queryguard.json");

// Rendered natively by almost every CI system.
await new QueryGuardJUnitReporter().WriteAsync(result, "artifacts/queryguard.junit.xml");
```

## Output is deterministic and versioned

Two runs over the same result produce byte-identical output, so a snapshot test on it is meaningful.
JSON carries an explicit `schemaVersion`: additive fields bump the minor version, and removing or
repurposing a field is a breaking change even in a preview. See
[ADR-0011](https://benziza.github.io/queryguard-dotnet/decisions/0011-versioning.html).

## Redaction cannot be bypassed

A reporter receives a result that was already redacted, so no reporter — including one you write —
can emit a parameter value or a connection string. That is enforced by construction rather than by
convention.

## Preview

Public APIs and the report schema may change before `1.0.0`. See the
[changelog](https://github.com/Benziza/queryguard-dotnet/blob/main/CHANGELOG.md).
