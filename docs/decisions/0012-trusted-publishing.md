# ADR-0012: Publish from a tagged release using short-lived credentials

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-052, QG-053

## Context

A NuGet API key with push rights to `QueryGuard.*` is, in practice, permission to ship code to
everyone who installs the package. Stored as a long-lived repository secret it is exposed to every
workflow that can read secrets, to anyone with the right repository access, and to any future
supply-chain mistake — and it expires only when someone remembers to rotate it.

nuget.org supports trusted publishing: the workflow exchanges a GitHub OIDC token for a
short-lived credential scoped to a specific repository and workflow. Nothing long-lived is stored.

QueryGuard is also a diagnostics library that reads SQL. Its supply chain is part of the trust it
asks for.

## Decision

**Publishing happens only from a `v*` tag, in a protected `release` environment, using
short-lived credentials obtained at publish time.**

- The release workflow triggers on a `v*` tag push. Nothing publishes from a branch, a
  pull request, or a manual run against arbitrary code.
- The job runs in the GitHub `release` environment, so environment protection rules — not just
  branch permissions — gate publication.
- Permissions are the minimum that works: `contents: write` to create the release,
  `id-token: write` for the OIDC exchange. Everything else stays read-only. The repository default
  for workflow tokens is read-only.
- The tag is verified against the packaged version before anything is pushed. A mismatch stops
  the release.
- Full build, test, and pack run on the tag commit. Packages are never published from an artifact
  built elsewhere.
- Third-party actions are pinned to full commit SHAs, with Dependabot proposing updates so pinning
  does not become stale pinning.
- Symbol packages (`.snupkg`) and SourceLink ship alongside, so a consumer can verify what they run
  against the tagged source.

If trusted publishing is unavailable at release time, the fallback is a narrowly scoped,
short-expiry API key limited to the `QueryGuard.*` package pattern, stored as an environment
secret on the `release` environment only, and revoked immediately after use. That is a documented
fallback, not the plan.

## Rejected alternatives

**A long-lived API key as a repository secret.** The common approach and the one this decision
exists to avoid, for the reasons above.

**Publishing from a local machine.** No audit trail, no reproducibility, and it ties releases to
one developer's environment. Also makes it impossible to prove what source a package was built from.

**Publishing on every merge to `main`.** Fast feedback, and it removes the deliberate act of
deciding to release. Releases should be intentional, tagged, and checklisted.

## Consequences

- The release workflow's contract depends on nuget.org's trusted publishing behavior, which must be
  verified before the first real publish rather than assumed from documentation.
- A dry run is required before the first release: build, test, and pack on a tag, with the push step
  proven not to publish accidentally.
- Setting up the `release` environment and the nuget.org trusted publishing policy is a manual,
  one-time step, recorded in the release checklist so it is not rediscovered later.
- Every release is auditable: tag, commit, workflow run, artifacts.

## Revisit when

- Trusted publishing behavior or availability changes.
- A second maintainer needs publish rights, which is an environment reviewer change rather than a
  shared secret.
