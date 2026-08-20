# QueryGuard.Cli

Records a query baseline from [QueryGuard.NET](https://github.com/Benziza/queryguard-dotnet) JSON
reports, and verifies later runs against it — so a query-count regression shows up in CI without any
baseline plumbing written by hand.

```bash
dotnet tool install -g QueryGuard.Cli
```

## The workflow

Your tests measure and write JSON reports:

```csharp
await new QueryGuardJsonReporter().WriteAsync(result, "artifacts/queryguard/companies.json");
```

Record what the code costs today, once, and commit the file:

```bash
queryguard baseline record
```

Then on every run:

```bash
queryguard verify --summary artifacts/queryguard/summary.md
```

```text
2 report(s), 2 scope(s) compared.
  REGRESSION GET /api/companies: 3 -> 51
             GET /api/users: unchanged

1 scope(s) run more queries than the baseline.
If that is intended, re-record the baseline and commit it.
```

`3 -> 51` needs no threshold to read, which is the point — `WithMaxQueries(10)` needs someone to know
that ten is right, and on an unmeasured endpoint nobody does.

## Commands

```text
queryguard baseline record [--reports <path>] [--baseline <file>]
queryguard verify [--reports <path>] [--baseline <file>] [--summary <file>] [--fail-on-regression]
```

| Option | Default | |
| --- | --- | --- |
| `--reports` | `artifacts/queryguard` | Directory, glob, or file holding the JSON reports |
| `--baseline` | `queryguard-baseline.json` | The committed baseline |
| `--summary` | — | Write the Markdown table here, for a job summary or a pull request comment |
| `--fail-on-regression` | off | Exit 2 when a scope runs more queries than the baseline |

Exit codes: `0` success — including a regression found without the flag; `1` bad usage or an unreadable
file; `2` a regression, with `--fail-on-regression`.

## Things worth knowing

**It reports by default and fails on request.** More queries is a fact; whether it is a defect is a
judgement. A new feature legitimately costs queries.

**Recording merges rather than replaces.** A run that measured three endpoints will not delete the
baseline for every endpoint it did not exercise.

**A new scope is not a regression**, and a scope missing from the run is ignored rather than reported as
removed — a filtered test run would otherwise claim every endpoint it skipped had been deleted.

**It does not run your tests.** Measurement happens in the test process where the `DbContext` lives. A
tool that owned that would have to guess your test command, your target framework, and your fixture
wiring.

**Files that are not QueryGuard reports are skipped**, so a coverage file in the same directory does not
stop the run.

## In GitHub Actions

The [QueryGuard action](https://github.com/Benziza/queryguard-dotnet/tree/main/action) posts the table as
a sticky pull request comment:

```yaml
- run: dotnet test
- run: queryguard verify --summary artifacts/queryguard/summary.md
- uses: Benziza/queryguard-dotnet/action@main
```

Full documentation:
[docs/baselines](https://github.com/Benziza/queryguard-dotnet/blob/main/docs/baselines/README.md).

## Preview

Public APIs and the baseline schema may change before `1.0.0`. The baseline document carries its own
`schemaVersion`, and a file written by a future major version is rejected rather than read optimistically
— a silently empty baseline would report every scope as new and hide every regression in the run.
