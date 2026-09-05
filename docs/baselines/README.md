# Baselines

A baseline saves current query counts. Later runs show what changed:

```text
GET /api/companies
  3 -> 51 queries
```

Use a baseline when you do not know the right query budget yet.
You can also use it alongside fixed budgets.

## Record and verify with the CLI

Install the tool:

```bash
dotnet tool install -g QueryGuard.Cli
```

Have your tests write JSON reports:

```csharp
await new QueryGuardJsonReporter().WriteAsync(result, "artifacts/queryguard/companies.json");
```

Run the tests, then record the baseline:

```bash
queryguard baseline record
```

Commit `queryguard-baseline.json`. After later test runs, compare the new reports:

```bash
queryguard verify --summary artifacts/queryguard/summary.md
```

Add `--fail-on-regression` to return a non-zero exit code when counts increase.
Without it, regressions are reported but do not fail the command.

The CLI reads reports; it does not run tests. Recording updates measured scopes and keeps
other baseline entries. Files that are not QueryGuard reports are skipped.

## The file

```json
{
  "schemaVersion": "1.0",
  "scopes": [
    {
      "scope": "GET /api/companies",
      "readCommands": 3,
      "distinctQueries": 2,
      "topFingerprintOccurrences": 2
    }
  ]
}
```

The file stores counts, not SQL or timings. Entries are sorted by scope name.

## Recording one in code

```csharp
var baseline = QueryGuardBaseline.Empty;

foreach (var result in measuredResults)
{
    baseline = baseline.Record(result);
}

File.WriteAllText("queryguard-baseline.json", baseline.ToJson());
```

`Record` returns a new baseline. Keep its return value.

## Comparing against one

```csharp
var baseline = QueryGuardBaseline.FromJson(File.ReadAllText("queryguard-baseline.json"));
var comparison = QueryGuardBaselineComparison.Compare(baseline, measuredResults);

if (comparison.HasRegressions)
{
    // Fail the test, log a warning, or publish a report.
}
```

`Compare` returns results without throwing for regressions.

## In a pull request

Render a Markdown summary:

```csharp
var markdown = new QueryGuardBaselineMarkdownReporter().Render(comparison);

var summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
if (summary is not null)
{
    File.AppendAllText(summary, markdown);
}
```

| Scope | Before | Now | Change |
| --- | --: | --: | --- |
| `GET /api/companies` | 3 | 51 | +48, most-repeated query +48 |
| `GET /api/orders` | 8 | 8 | most-repeated query +7 |
| `GET /api/users` | 4 | 4 | unchanged |
| `GET /api/reports` | 12 | 3 | -9 (improved) |

A stable total can still hide more repetition, as the `orders` row shows.
The baseline tracks the highest occurrence count separately.

For a pull request comment, see the
[GitHub Action setup](https://github.com/Benziza/queryguard-dotnet/blob/main/action/README.md).

## Comparison rules

- New scopes are shown as new, not as regressions.
- Scopes missing from a run are ignored.
- Lower counts are reported as improvements.
- To accept an intended increase, record again and commit the diff.
- Renaming a scope makes it a new entry; names are the comparison key.
- Unsupported future schema versions are rejected.
- To resolve a baseline merge conflict, rerun the relevant tests and record their results.

See [baseline design](../decisions/0013-baseline-storage.md) for details.