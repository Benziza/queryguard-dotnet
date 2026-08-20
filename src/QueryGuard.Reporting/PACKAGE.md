# QueryGuard.Reporting

Render a [QueryGuard.NET](https://github.com/Benziza/queryguard-dotnet) result as console text,
versioned JSON, JUnit XML, or SARIF.

```csharp
var result = await scope.CompleteAsync();

// Human-readable, for a terminal.
Console.Write(new QueryGuardConsoleReporter().Render(result));

// Machine-readable, with an explicit schema version.
await new QueryGuardJsonReporter().WriteAsync(result, "artifacts/queryguard.json");

// Rendered natively by almost every CI system.
await new QueryGuardJUnitReporter().WriteAsync(result, "artifacts/queryguard.junit.xml");

// For GitHub code scanning: an annotation on the line that ran the query.
await new QueryGuardSarifReporter(repositoryRoot).WriteAsync(result, "artifacts/queryguard.sarif");
```

## SARIF puts a finding on the diff

Upload the file and a repeated query appears as an annotation on the line that caused it, in the viewer
CodeQL already uses — no dashboard, nothing to install:

```yaml
- uses: github/codeql-action/upload-sarif@v4
  with:
    sarif_file: artifacts/queryguard.sarif
    category: queryguard
```

The job needs `security-events: write`. Pass the repository root to the reporter: the paths a stack trace
records are absolute, and only a repository-relative path can be matched against a diff. Without it the
finding still appears, just without the annotation.

A candidate is reported as a **warning**, never an error, whatever the policy severity says about failing
the build. Failing a build on evidence is how a check gets switched off rather than tuned. An allowlisted
finding becomes a SARIF *suppression* carrying its reason, rather than being dropped — the repetition is
still there, and the report should not imply otherwise.

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
