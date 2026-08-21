# QueryGuard.NET documentation

## Using QueryGuard

- [How it works](./concepts/README.md): sessions, the stateless interceptor, fingerprints, redaction,
  and analysis, in the order a command travels through them.
- [Testing](./testing/README.md): measure `WebApplicationFactory` requests or open an explicit scope
  around a service or background job.
- [Public project validation](./case-studies/public-project-validation.md): results from three public
  ASP.NET Core test suites, including the package problem the work found.
- [Configuration](./configuration/README.md): every budget and option, what it defaults to, and the
  reasoning behind each default.
- [Baselines](./baselines/README.md): record what a scope costs today and report what changed, so
  nobody has to guess a budget number.
- [Troubleshooting](./troubleshooting/README.md): nothing recorded, fingerprints not grouping,
  `(unmatched)` scope names, dropped commands.
- [When a finding is wrong](./troubleshooting/false-positives.md): what repeated SQL does and does not
  prove, and the four ways to record an intentional exception.
- [Provider support](./providers/README.md): what is integration-tested, what is fixture-verified,
  and why those are different claims.
- [Benchmarks](./benchmarks.md): what QueryGuard costs on the command path, with raw output and the
  reasons not to turn any of it into a production latency figure.

## Understanding the design

- [Architecture decision records](./decisions/README.md): why QueryGuard behaves the way it does, and
  what would make each decision change. The most complete explanation of the design.
- [Roadmap](./roadmap.md): what v0.1 covers, what it deliberately does not, and how priority is
  decided.

## For contributors

- [CONTRIBUTING.md](../CONTRIBUTING.md): workflow, branch and commit conventions, and how to get a
  change merged.
- [Coding standards](./coding-standards.md): the rules that come up in review, with the reasoning
  behind them.
- [Testing strategy](./testing-strategy.md): the layers, the critical scenarios that must never
  regress, and the benchmark honesty rules.
- [Review checklist](./review-checklist.md): prompts for changes that touch capture, redaction,
  the hot path, or the public API. Not required for a pull request.
- [Releasing](./releasing.md): one-time setup, the per-release checklist, and
  what to do when a publish goes wrong.

## Assets

`assets/` holds the logo, package icon, and social preview image. They are generated from simple shapes
rather than a design tool so they stay editable and reviewable in a diff.
