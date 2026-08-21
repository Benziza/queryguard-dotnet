# ADR-0013: A baseline is a committed file, compared against the merge base

- **Status:** Accepted
- **Date:** 2026-08-20
- **Deciders:** Mohamed Benziza
- **Related:** R-012

## Context

Every budget QueryGuard offers asks the user for a number they usually do not have.

```csharp
policy.WithMaxQueries(10)
```

Ten what? On an endpoint nobody has measured, the honest answer is that nobody knows. So the number
gets guessed, set high enough to never fire, or raised until the build goes green. A threshold that
gets raised whenever it fires is not a guard.

A baseline asks for nothing. It records what the code does today and reports what changed:

```text
GET /api/companies
  3 -> 51 queries
```

That needs no threshold and no knowledge of what good looks like. It is also the shape of the event a
reviewer actually cares about: not "this endpoint is over budget" but "this pull request changed this
endpoint".

The design question is where the "before" number comes from.

## Decision

**The baseline is a JSON file committed to the repository. The comparison happens against whatever
that file says on the branch being built.**

- One file, `queryguard-baseline.json` by convention, holding one entry per measured scope.
- Each entry records **counts only**: read commands, distinct fingerprints, and how many times the
  most repeated fingerprint ran.
- Entries are ordered by scope name and the file ends with a newline, so its diff is stable and
  reviewable.
- The document carries a `schemaVersion`. A file written by a future major version is **rejected**,
  not read optimistically.
- Accepting a regression is a deliberate act: regenerate the file and commit it. The diff then shows a
  reviewer that a scope went from 3 to 51 and somebody decided that was fine.
- A scope with no entry is **new**, not a regression. A pull request that adds an endpoint must not
  fail for adding it.
- A scope in the baseline but absent from the run is **ignored**, not reported as removed. A filtered
  test run would otherwise claim every endpoint it did not exercise had been deleted.

### Counts only, and no timings

A baseline recording SQL text would be a second copy of the report, would need redaction rules of its
own, and would produce a diff on every unrelated schema change, so nobody would read it.

Durations are excluded for a stronger reason: they vary between a laptop and a shared runner, so a
baseline containing them would report a regression whenever CI was busy. That is the failure mode that
teaches people to ignore a tool, and it is the same reasoning that keeps duration budgets off by
default ([ADR-0007](./0007-stack-trace-policy.md) has the equivalent argument for stack traces).

### Why the most-repeated count is stored separately

Because it moves when the total does not. Replacing twenty distinct lookups with one query repeated
twenty times leaves the read count identical and is exactly the regression QueryGuard exists to catch.
A baseline that only stored totals would miss it, which would make it strictly worse than the budget it
is meant to complement.

## Rejected alternatives

**A hosted service or database.** The comparison would be against "the last run that reported", which
is not the merge base and is not reproducible. It also means QueryGuard cannot answer the question
without a network call and an account, which is a different product.

**A CI cache keyed on the branch.** Tempting, and it avoids a committed file. But a cache is evictable
and invisible: a regression would be reported or not depending on whether the cache happened to be
warm, and nobody could tell from the repository what the baseline was. A committed file is auditable
by reading it.

**Git history: measure the merge base by checking it out and running it.** The most correct answer,
and it doubles every CI run and requires the old commit to still build. Worth revisiting if the
committed file turns out to cause merge conflicts often enough to matter.

**Automatically updating the baseline on merge to `main`.** Removes the friction of regenerating it,
and removes the review step that gives the file its value. A regression would be silently absorbed by
the next merge.

**Failing the build on any regression, by default.** Rejected for the same reason a repeated-query
finding is a warning rather than a failure: more queries is a fact, whether it is a defect is a
judgement, and a tool that fails a build for adding a legitimate feature gets switched off. The
comparison reports; the caller decides what to do with `HasRegressions`.

## Consequences

- The baseline file will occasionally conflict on merge. It is machine-generated and ordered, so
  regenerating is always a valid resolution.
- Two branches that both change the same scope will each show a regression against `main`, and the
  second to merge has to regenerate. That is the correct outcome and it is mildly annoying.
- Scope names are the join key, so renaming a route reads as one scope disappearing and another
  appearing. Both are non-events by the rules above, which means a rename silently loses that scope's
  history. Acceptable: the alternative is tracking identity across renames, which needs more
  information than a route pattern carries.
- The comparison is a library API, not a command-line tool. Producing the file and posting the comment
  is the caller's job: a test that writes Markdown, and a CI step that appends it to
  `$GITHUB_STEP_SUMMARY`.

## Revisit when

- Merge conflicts on the baseline file become a real complaint rather than a predicted one.
- Someone wants per-environment baselines, which the current single-file shape does not express.
- A user asks for the merge-base measurement instead of a committed file, which would be evidence that
  the file's staleness matters more than its auditability.
