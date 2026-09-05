# How QueryGuard works

QueryGuard captures EF Core commands inside a scope and checks them when the scope ends.

```text
EF Core command
  → Active scope?
      No  → Skip capture
      Yes → Normalize SQL → Redact values → Create fingerprint → Record command
  → Scope ends → Group queries → Check policy → Return result
```

## 1. The session is the unit of measurement

A session groups commands for one request, test, or job:

- `app.UseQueryGuard()` opens a session for each measured request.
- `QueryGuardScope.Start(...)` opens an explicit scope.
- `TrackQueries(...)` measures requests in integration tests.

Nested scopes capture into the innermost session. Without an active session, nothing is captured.
A completed session cannot be changed.

## 2. The interceptor is stateless

The interceptor observes commands without changing SQL, results, or application exceptions.
It gets the active session from `IQueryGuardSessionAccessor`.

The default accessor uses `AsyncLocal<T>`. Sessions flow through `await` and `Task.Run`.
Code that suppresses execution context flow needs extra setup.
For `TestServer`, use `TrackQueries` or follow the
[manual setup](../troubleshooting/README.md#4-testserver-is-not-flowing-executioncontext).

See [session design](../decisions/0002-session-propagation.md).

## 3. A fingerprint decides what "the same query" means

A fingerprint is a query ID such as `QG-FP-1A2B3C4D`. QueryGuard creates it by:

1. Collapsing whitespace and removing comments except QueryGuard directives.
2. Replacing parameter references with a common placeholder.
3. Redacting values according to the capture settings.
4. Hashing the normalized, redacted SQL.

Token order, aliases, and quoted identifiers stay unchanged. SQL from different providers can
have different IDs.

The built-in redactor hashes the full SQL before shortening it for display.
Two long queries can show the same shortened text but have different IDs.
See [display limits](../configuration/README.md#sql-display-length-and-fingerprints).

## 4. Redaction happens before reporting

By default, SQL string and number values are redacted, parameter values are not captured,
and retained SQL samples are limited. Connection strings are not captured.

Request stack traces are off by default. Test scopes capture query origins by default.
See [capture settings](../configuration/README.md#capture-and-privacy).

## 5. Analysis happens when the scope ends

QueryGuard groups recorded commands by fingerprint and checks the configured policy.
The result contains counts, durations, SQL samples, and findings in a stable order.

Repeated SQL is a possible N+1 problem. A repetition warning alone does not fail a test;
a rule with failure severity does. See [budgets](../configuration/README.md#budgets).

## Next steps

- [Testing](../testing/README.md)
- [Configuration](../configuration/README.md)
- [Intentional repetition](../troubleshooting/false-positives.md)
- [Design decisions](../decisions/README.md)