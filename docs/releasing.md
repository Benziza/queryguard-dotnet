# Releasing

A published NuGet version is immutable. It can be unlisted, so it stops appearing in search and in
`latest` resolution, but anyone who already depends on it keeps resolving it forever. That single fact
shapes everything below: every check that can happen before a push happens before a push, and the
publish step itself does as little thinking as possible.

The workflow is [`.github/workflows/release.yml`](../.github/workflows/release.yml). The reasoning
behind it is [ADR-0012](./decisions/0012-trusted-publishing.md).

## How the workflow is shaped

Two jobs, and the split is the safety mechanism.

**`verify`** runs on a `v*` tag *and* on a manual dispatch. It resolves the version, checks the tag
against it, restores, builds, runs the whole test suite, packs the `src/` projects, and runs
[`eng/verify-packages.sh`](../eng/verify-packages.sh) — which asserts the package metadata and then
installs the packed packages into a throwaway project and runs code against them. It uploads
everything it produced.

**`publish`** runs only when the ref is a tag. It downloads the artifact `verify` produced, exchanges
the workflow's OIDC token for a short-lived nuget.org credential, pushes, and creates the GitHub
release.

Two consequences worth stating plainly:

- **A manual run cannot publish.** There is no input to turn publishing on. `publish` is gated on the
  ref being a tag, so a rehearsal cannot become a release by ticking the wrong box.
- **What ships is what was verified.** `publish` does not check out source and does not build. It
  pushes the exact bytes `verify` checked, so "the tested packages" and "the published packages"
  cannot drift apart.

## One-time setup

These are manual, and they are the part most likely to be rediscovered painfully later.

1. **Create the `release` environment** in repository settings. Add whatever protection rules make
   sense for the number of maintainers — for a single maintainer, a required reviewer still adds a
   deliberate pause between pushing a tag and shipping to everyone.
2. **Configure a trusted publishing policy on nuget.org** for each `QueryGuard.*` package, bound to
   this repository and the `Release` workflow. Until the packages exist, nuget.org allows a policy
   for a package ID that has not been published yet — use that for the first release rather than
   falling back to an API key.
3. **Add the `NUGET_USER` secret** with the nuget.org account name. This is not a credential; the
   credential is obtained per-run through OIDC.
4. **Check the required status checks** on the `main` ruleset include every CI job. New jobs do not
   get added automatically, so a job can be silently non-blocking.

If trusted publishing is unavailable when a release is due, the documented fallback is a narrowly
scoped, short-expiry key restricted to the `QueryGuard.*` glob, stored as a secret on the `release`
environment only, and revoked immediately afterwards. That is a fallback, not a plan.

## Per-release checklist

Rehearse first. A dry run costs a few minutes and has caught real problems here.

1. **Dry run.** Trigger the `Release` workflow manually from the branch you intend to tag. `verify`
   runs end to end, including the consumer smoke test, and nothing is published. **Read the resolved
   version in its log** rather than trusting a green tick: a rehearsal has no tag to compare against,
   so it validates only that the version resolves and is shaped like a semantic version. A wrong-but-
   well-formed version still passes here and fails on the tag.
2. **Version.** Set `VersionPrefix` and `VersionSuffix` in `Directory.Build.props`. A preview keeps a
   suffix (`preview.2`); a stable release removes it. The tag must equal the resulting version with a
   `v` prefix, or `verify` fails on purpose.
3. **Changelog.** Move `## [Unreleased]` entries under the new version with today's date. Breaking
   changes get migration notes, not just a mention. The generated GitHub release notes list merged
   pull requests; `CHANGELOG.md` is the curated record.
4. **Report schema.** If any reporter output changed shape, confirm `QueryGuardJsonReporter.SchemaVersion`
   moved with it. Additive fields bump the minor; removing or repurposing a field is breaking even in
   a preview. See [ADR-0011](./decisions/0011-versioning.md).
5. **Benchmarks.** If anything on the capture path changed, re-run the suite and update
   [`docs/benchmarks.md`](./benchmarks.md) with the new numbers, raw output, and source commit. A
   stale performance page is worse than none.
6. **Merge, then tag the merge commit.**

   ```bash
   git tag -a v0.1.0-preview.1 -m "QueryGuard.NET 0.1.0-preview.1"
   git push origin v0.1.0-preview.1
   ```

7. **Approve the `release` environment** if a reviewer is required, and watch the run.
8. **Verify what shipped.** Install the package into a scratch project from nuget.org — not from a
   local feed — and check the README, icon, and license render on the package page. Confirm the
   symbol package is listed.

## When something goes wrong

**The tag does not match the version.** `verify` fails before anything is packed. Delete the tag,
fix `Directory.Build.props`, and tag again:

```bash
git push --delete origin v0.1.0-preview.1
```

**Package verification fails.** Nothing has been published; `publish` never starts. Fix forward on
`main` and cut a new tag. Do not reuse the tag — a tag that once pointed at a different commit is a
lie in the history for anyone who already fetched it.

**A publish half-succeeded.** Some packages pushed, some did not. Re-run the `publish` job:
`dotnet nuget push --skip-duplicate` succeeds for the ones already on nuget.org, so a re-run
completes the set rather than failing on the first duplicate. This is why the flag is there.

**A bad version reached nuget.org.** It cannot be replaced. Unlist it, publish a fixed version, and
say what happened in `CHANGELOG.md`. Unlisting stops new consumers finding it; it does nothing for
anyone who already depends on it. This is the outcome every check above exists to avoid.

## A note on why the tag check earns its keep

The first real tag push failed here, correctly. The version-resolution step had parsed
`Directory.Build.props` with a regex containing a variable-length lookbehind — invalid in PCRE — so
`grep` failed, a `|| true` swallowed the failure, the version suffix silently became empty, and the step
resolved `0.1.0` for a tag that said `0.1.0-preview.1`. Nothing was published, because the tag
comparison refused it.

Two lessons are now built into the workflow. The version comes from MSBuild rather than from parsing
XML, so it is the same property `dotnet pack` stamps on the package and the two cannot disagree. And a
dry run validates the version it resolved instead of ignoring it, because the original bug passed two
rehearsals without complaint — a check that only runs when a tag exists is a check that cannot protect
the rehearsal.
