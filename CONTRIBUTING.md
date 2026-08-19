# Contributing to QueryGuard.NET

Thank you for considering a contribution. QueryGuard is a small, deliberately focused
library, and the fastest way to get a change merged is to keep it focused too.

## Table of contents

- [Ground rules](#ground-rules)
- [Before you write code](#before-you-write-code)
- [Development environment](#development-environment)
- [Building and testing](#building-and-testing)
- [Coding standards](#coding-standards)
- [Privacy rules for contributions](#privacy-rules-for-contributions)
- [Branch, commit, and pull request conventions](#branch-commit-and-pull-request-conventions)
- [What makes a good first contribution](#what-makes-a-good-first-contribution)
- [Reporting a false positive](#reporting-a-false-positive)
- [Security issues](#security-issues)

## Ground rules

1. **Issue first.** Every behavior change starts with an accepted issue. This keeps the
   scope, the acceptance criteria, and the decision history public.
2. **One behavior per pull request.** A PR that adds a detector *and* refactors the
   session model is two PRs.
3. **Tests describe behavior.** A bug fix without a failing-then-passing test is not a
   fix, it is a guess.
4. **Never claim more than the evidence supports.** QueryGuard reports *potential* N+1 and
   repeated-query candidates. Wording that promises certainty will be changed in review.
5. **Capture less by default.** Any new captured field needs a privacy review and a
   redaction test.

## Before you write code

- Search the [issues](https://github.com/Benziza/queryguard-dotnet/issues) and
  [discussions](https://github.com/Benziza/queryguard-dotnet/discussions) first.
- For a new capability, open a **Feature request** and describe the workflow pain before
  proposing an API. The problem statement matters more than the code sketch.
- For unexpected behavior, open a **Bug report** with a minimal synthetic reproduction.
- For a legitimate query pattern that QueryGuard flags, use the
  **False-positive report** form. These are the highest-value reports the project receives.
- Comment on the issue to claim it. Wait for `status:accepted` before investing
  significant time — it protects you from writing code that will not be merged.

## Development environment

| Requirement | Version |
| --- | --- |
| .NET SDK | 10.0.100 or later (see [`global.json`](./global.json)) |
| .NET 8 runtime | Required only to execute the `net8.0` test pass locally |
| Docker | Required only for the PostgreSQL provider suite |

QueryGuard multi-targets `net8.0` and `net10.0`. The `net10.0` SDK can *build* both, but
running the `net8.0` test pass locally needs the .NET 8 runtime installed. If you only have
the .NET 10 runtime, run tests with `-f net10.0` and let CI cover `net8.0`.

```bash
git clone https://github.com/Benziza/queryguard-dotnet.git
cd queryguard-dotnet
dotnet restore QueryGuard.slnx
```

## Building and testing

These are exactly the commands CI runs. Run them before marking a PR ready.

```bash
dotnet format QueryGuard.slnx --verify-no-changes
dotnet build QueryGuard.slnx -c Release
dotnet test QueryGuard.slnx -c Release
dotnet pack QueryGuard.slnx -c Release -o artifacts/packages
```

Useful subsets:

```bash
# Only the framework you have a runtime for
dotnet test QueryGuard.slnx -c Release -f net10.0

# A single project
dotnet test tests/QueryGuard.Core.Tests/QueryGuard.Core.Tests.csproj

# Reproduce the sample demo from the README
dotnet test samples/QueryGuard.SampleTests/QueryGuard.SampleTests.csproj
```

The PostgreSQL provider suite starts a container through Testcontainers. It skips itself
automatically when Docker is unavailable, so a missing Docker daemon will not fail your
local run — but it *will* run in CI.

## Coding standards

The full rationale lives in [`docs/coding-standards.md`](./docs/coding-standards.md).
The rules that most often come up in review:

- **Warnings are errors.** `TreatWarningsAsErrors` is on. Do not suppress a warning
  without a comment explaining why the suppression is correct.
- **Nullable reference types are enabled** and every public API is annotated.
- **Public API is documented.** Missing XML documentation on a public member fails the build.
- **The interceptor is stateless.** Request and test state lives in the QueryGuard session,
  reached through `IQueryGuardSessionAccessor`. Never add static mutable session state.
- **Never block on async work.** No `.Result`, no `.Wait()`. Implement both the sync and
  async interceptor paths, and forward `CancellationToken`.
- **`ConfigureAwait(false)` in library code.** Enforced by analyzer.
- **Do not hide application behavior.** QueryGuard observes. It must never modify the
  generated SQL, suppress a command, alter a response, or replace the original exception.
- **Public collections are read-only** (`IReadOnlyList<T>`, not `List<T>`).
- **Structured logging only.** Use event IDs and message templates, not interpolated strings.

Formatting is not a review topic: `dotnet format` is the single source of truth and it
runs as a required check.

## Privacy rules for contributions

QueryGuard reads SQL, so privacy is a product feature rather than a policy document.

- Parameter values are **not** captured by default and must stay that way.
- Connection strings are never captured, logged, or serialized.
- Redaction is applied centrally, before any reporter writes output. A reporter must not
  be able to bypass it.
- Tests, samples, fixtures, and documentation must use **synthetic** schemas and data.
  Do not contribute SQL, table names, or output taken from a real employer or customer system.

## Branch, commit, and pull request conventions

| Item | Convention | Example |
| --- | --- | --- |
| Branch | `<type>/QG-<id>-<short-kebab-description>` | `feat/QG-021-query-budget-policy` |
| Commit | `<type>(<scope>): <imperative summary>` | `feat(core): add repeated-query budget` |
| PR title | `<type>(<scope>): <summary> [QG-###]` | `fix(efcore): isolate parallel sessions [QG-014]` |

Types: `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `chore`, `ci`.
Scopes: `core`, `efcore`, `fingerprint`, `detector`, `policy`, `aspnetcore`, `testing`,
`reporting`, `provider`, `packaging`, `release`, `repo`, `sample`, `docs`.

Pull requests:

- Open as a **draft** early. A visible scope invites feedback before the code is finished.
- Fill in the pull request template. The privacy and performance sections are not optional.
- Target **under 400 changed lines**. Above roughly 800 (excluding generated files),
  explain in the description why the change cannot be split.
- Merges are **squash only**, so the PR title becomes the commit message. Write it as a
  changelog entry.
- All required checks must pass and all conversations must be resolved before merge.

## What makes a good first contribution

Look for [`good first issue`](https://github.com/Benziza/queryguard-dotnet/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22).
Particularly welcome:

- **Provider SQL fixtures.** A synthetic SQL sample from a provider we do not test yet is
  directly useful to the fingerprint normalizer.
- **False-positive scenarios.** A repeated query that is intentional and bounded, expressed
  as a test, makes the defaults better for everyone.
- **Redaction tests.** Any input that could leak a value through a report is a bug worth a test.
- **Documentation and sample improvements**, especially anything that shortened your own
  time to a first result.

## Reporting a false positive

Use the false-positive issue form and include the fingerprint ID, occurrence count,
redacted normalized SQL, and — most importantly — *why the repetition is intentional*.
Accepted reports become regression fixtures so the behavior stays fixed.

## Security issues

Do not open a public issue for a vulnerability. Follow [SECURITY.md](./SECURITY.md).

## Code of conduct

This project follows the [Contributor Covenant](./CODE_OF_CONDUCT.md). By participating you
agree to uphold it.
