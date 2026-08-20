# Baselines

A query budget asks you for a number you probably do not have. `WithMaxQueries(10)` needs someone to
know that ten is right, and on an endpoint nobody has measured, nobody does — so the number gets
guessed, or set high enough never to fire, or set low and then raised until the build goes green.

A baseline asks for nothing. It records what the code does today and reports what changed.

```text
GET /api/companies
  3 -> 51 queries
```

No threshold, no judgement needed to read it. And it is the shape of the thing a reviewer actually
cares about: not *this endpoint is over budget* but *this pull request changed this endpoint*.

Budgets and baselines are complementary. A budget is a line you refuse to cross. A baseline notices you
moved.

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

Counts only — no SQL, no timings. SQL would make the file a second copy of the report and would produce
a diff on every unrelated schema change. Timings vary between a laptop and a busy runner, so a baseline
containing them would report a regression whenever CI was loaded, which is how a check earns being
ignored.

Entries are ordered by scope and the file ends with a newline, so its diff is reviewable. Commit it.

## Recording one

```csharp
var baseline = QueryGuardBaseline.Empty;

foreach (var result in measuredResults)
{
    baseline = baseline.Record(result);
}

File.WriteAllText("queryguard-baseline.json", baseline.ToJson());
```

`QueryGuardBaseline` is immutable — `Record` returns a new instance — so building one across a test run
is safe without locking.

## Comparing against one

```csharp
var baseline = QueryGuardBaseline.FromJson(File.ReadAllText("queryguard-baseline.json"));
var comparison = QueryGuardBaselineComparison.Compare(baseline, measuredResults);

if (comparison.HasRegressions)
{
    // Your call. Fail the test, warn, or just publish the table.
}
```

`Compare` does not fail anything. More queries is a fact; whether it is a defect is a judgement, and
the library does not get to make it — the same reason a repeated-query finding is a warning rather than
a failure.

## In a pull request

```csharp
var markdown = new QueryGuardBaselineMarkdownReporter().Render(comparison);

var summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
if (summary is not null)
{
    File.AppendAllText(summary, markdown);
}
```

Which produces this on the workflow run page:

> ### QueryGuard
>
> **2 scopes now run more queries than the baseline.** `GET /api/companies` went from 3 to 51.
>
> | Scope | Before | Now | Change |
> | --- | --: | --: | --- |
> | `GET /api/companies` | 3 | 51 | +48, most-repeated query +48 |
> | `GET /api/orders` | 8 | 8 | most-repeated query +7 |
> | `GET /api/invoices` | — | 3 | new scope |
> | `GET /api/users` | 4 | 4 | unchanged |
> | `GET /api/reports` | 12 | 3 | -9 (improved) |

The `GET /api/orders` row is the one worth looking at twice. The read count did not move — **8 before,
8 now** — but one query is now running seven more of them than it used to. Twenty distinct lookups
becoming one query repeated twenty times leaves a total-count budget perfectly satisfied. That is why
`topFingerprintOccurrences` is stored separately.

## Rules that keep it usable

**A new scope is not a regression.** Otherwise the pull request that adds any endpoint fails for adding
it, and the check gets disabled by the second person who hits it.

**A scope missing from the run is ignored, not reported as removed.** A filtered test run would
otherwise claim every endpoint it did not exercise had been deleted.

**Improvements are reported too.** A pull request that fixes an N+1 gets to show it. A tool that only
delivers bad news is one people stop reading.

**Accepting a regression is deliberate.** Regenerate the file and commit it. The diff then shows a
reviewer that a scope went from 3 to 51 and somebody decided that was fine — which is a much better
record than a threshold quietly raised in a config file.

## Things to know

- **The file will occasionally conflict on merge.** It is generated and ordered, so regenerating is
  always a valid resolution.
- **Renaming a route loses that scope's history.** Scope names are the join key, so a rename reads as
  one scope disappearing and another appearing — both non-events by the rules above.
- **A future schema version is rejected, not guessed at.** Reading a baseline wrong is worse than
  refusing to read it: a silently empty baseline reports every scope as new, which hides every
  regression in the run.

The reasoning behind all of this is in [ADR-0013](../decisions/0013-baseline-storage.md).
