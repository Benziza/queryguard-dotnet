# QueryGuard GitHub Action

Publishes a QueryGuard query-count report to the job summary and, on a pull request, to a **sticky
comment**: one comment that gets edited rather than a new one per push.

```yaml
- uses: Benziza/queryguard-dotnet@v0.1.0-preview.5
```

That is the whole thing.

The root form starts with `v0.1.0-preview.5`. Older releases keep the compatible
`Benziza/queryguard-dotnet/action@<tag>` path.

## What it looks like

> ### QueryGuard
>
> **1 scope now runs more queries than the baseline.** `GET /api/companies` went from 3 to 51.
>
> | Scope | Before | Now | Change |
> | --- | --: | --: | --- |
> | `GET /api/companies` | 3 | 51 | +48, most-repeated query +48 |
> | `GET /api/orders` | 8 | 8 | most-repeated query +7 |
> | `GET /api/users` | 4 | 4 | unchanged |

No threshold to read, and `3 → 51` needs no explanation.

## Full example

Your tests measure, the tool compares, the action publishes.

```yaml
jobs:
  test:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      pull-requests: write   # required for the comment
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.x

      - run: dotnet test

      - run: dotnet tool install -g QueryGuard.Cli --prerelease
      - run: queryguard verify --summary artifacts/queryguard/summary.md

      - uses: Benziza/queryguard-dotnet@v0.1.0-preview.5
        with:
          summary-path: artifacts/queryguard/summary.md
```

The tests only need to write their JSON reports:

```csharp
await new QueryGuardJsonReporter().WriteAsync(result, "artifacts/queryguard/companies.json");
```

The [CLI](https://www.nuget.org/packages/QueryGuard.Cli) reads those, compares them against the
committed baseline, and renders the table. Rendering it in the test process instead works too, and is
the right call when the comparison needs anything the tool does not expose:

```csharp
var comparison = QueryGuardBaselineComparison.Compare(baseline, results);
var markdown = new QueryGuardBaselineMarkdownReporter().Render(comparison);

Directory.CreateDirectory("artifacts/queryguard");
await File.WriteAllTextAsync("artifacts/queryguard/summary.md", markdown);
```

### Use a root-anchored path

The snippet above is simplified. A test host runs with its **output folder** as the working directory,
so a relative `artifacts/queryguard/summary.md` actually lands in
`bin/Release/net10.0/artifacts/queryguard/`, and the action, looking in the workspace root, finds
nothing and reports that there was nothing to publish. Silently missing rather than wrong, which is the
worse of the two.

Anchor it somewhere you recognise:

```csharp
var root = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? FindRepositoryRoot();
var path = Path.Join(root, "artifacts", "queryguard", "summary.md");
```

`samples/QueryGuard.SampleTests/BaselineComparisonDemoTests.cs` walks up for the solution file, which
works the same on a laptop and in CI.

See [baselines](../docs/baselines/README.md) for recording the baseline, and
[`samples/QueryGuard.SampleTests`](../samples/QueryGuard.SampleTests) for the complete example:
this repository runs this action on its own pull requests.

## Inputs

| Input | Default | What it does |
| --- | --- | --- |
| `summary-path` | `artifacts/queryguard/summary.md` | Markdown file to publish. A glob is allowed; matches are concatenated in sorted order |
| `github-token` | `${{ github.token }}` | Token for the comment. Needs `pull-requests: write` |
| `comment` | `true` | Set `false` to publish only the job summary |
| `title` | `QueryGuard` | Identifies the comment so it is updated rather than duplicated |
| `fail-on-missing` | `false` | Whether a missing report fails the step |

## Things it deliberately does not do

**It does not fail your build.** Not for a missing report, not for a regression, not for a failed
comment. A regression is a fact your tests decide what to do about; a diagnostics tool that reddens CI
because it could not post a comment has cost more than it delivered. `fail-on-missing: true` is the one
opt-in.

**It does not post one comment per push.** The comment carries a hidden marker keyed on `title`, so it
is found and edited. A ten-commit branch gets one comment, not ten identical ones, which is how a bot
earns being muted. Two QueryGuard runs in one workflow can each own a comment by using different
titles.

**It does not run your tests.** Measurement happens inside your test process, where the DbContext is.
An action that tried to own that would have to guess your test command, your target framework, and how
your fixtures are wired.

## On forks

A pull request from a fork gets a read-only `GITHUB_TOKEN` by design, so the comment cannot be posted.
The action warns and exits successfully: the **job summary still carries the report**, so nothing is
lost. Using `pull_request_target` to work around this would run the fork's code with a writable token,
which is a known way to leak repository secrets. Not worth a comment.

## Why a composite action

No JavaScript bundle to commit and no Docker image to pull. `gh` and `jq` are preinstalled on every
GitHub-hosted runner, so this is one bash script with no dependencies, which matters for a tool whose
main argument is that it does not change how your build behaves.
