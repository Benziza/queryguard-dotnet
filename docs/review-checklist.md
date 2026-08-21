# Review checklist

**Not required for a pull request.** The template asks for three things: what changed, why, and how
it was tested, and that is enough for most changes, including every docs fix and typo.

This page is for the changes where being systematic pays: anything touching query capture, redaction,
the hot path, or the public API. Read it as prompts, not as boxes to tick.

It used to be the pull request template. Forty-five checkboxes in front of a first-time contributor
fixing a typo is a good way to never receive the fix, so it moved here.

## Privacy and capture

The one section worth being pedantic about, because a mistake here ships user data to a CI artifact
and sometimes to a public issue.

- No connection strings, credentials, private URLs, or real customer data anywhere, including tests.
- SQL in tests and docs is synthetic or redacted.
- Parameter values stay disabled by default.
- A newly captured field is documented and covered by a redaction test.
- The application's own exception and response are unchanged. See
  [ADR-0006](./decisions/0006-aspnet-observe-only.md).

## The hot path

- Allocations and synchronization on the per-command path were considered.
- Stack-trace capture stays optional and bounded to one trace per fingerprint
  ([ADR-0007](./decisions/0007-stack-trace-policy.md)).
- A performance claim in docs has a measured number behind it, or it does not go in.
- If the capture path changed, the benchmark was re-run and
  [docs/benchmarks.md](./benchmarks.md) updated with the new numbers and raw output.

## Correctness under concurrency

- Session isolation still holds. The stress suites use deliberately different per-scope counts, so
  a leak changes a total rather than hiding.
- Nothing static and mutable was introduced on the capture path.
- A flake was diagnosed, not retried.

## Public API and compatibility

- A public API change is deliberate, documented with XML comments, and called out in the PR.
- Both `net8.0` and `net10.0` build and test.
- Report schema compatibility is preserved, or `QueryGuardJsonReporter.SchemaVersion` moved with it
  ([ADR-0011](./decisions/0011-versioning.md)).
- Breaking-change risk is stated plainly.

## Findings and wording

- A finding says *candidate* or *potential*, never "N+1 detected"
  ([ADR-0003](./decisions/0003-detector-terminology.md)).
- An allowlist mechanism still requires a reason.
- Ignored findings are still reported as ignored rather than hidden.

## Provider behaviour

- Provider-specific SQL is covered by a fixture or an integration test.
- A support tier in [docs/providers/README.md](./providers/README.md) still matches what CI actually
  runs. "Integration-tested" and "fixture-verified" are different claims.
