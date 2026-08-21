# ADR-0004: Parameter values are never captured by default

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-013, R-003, [ADR-0007](./0007-stack-trace-policy.md)

## Context

QueryGuard sits on the database command path. Everything flowing through there is potentially
sensitive: parameter values are user data, connection strings are credentials, and SQL text can
reveal a private schema.

It is also a tool whose output is *meant to be shared*: pasted into a pull request, attached to
a CI run, uploaded as a JUnit report, or copied into a GitHub issue. Any data QueryGuard captures
should be assumed to end up somewhere public.

Richer capture would genuinely help. Knowing that a repeated query ran with 51 *different* key
values is much stronger evidence of an N+1 than knowing it ran 51 times. That is a real
diagnostic loss, and it is being accepted deliberately.

## Decision

**Capture the minimum that supports the finding, and enforce it centrally.**

Defaults, which are part of the package's public contract:

| Data | Default | Rationale |
| --- | --- | --- |
| Command text (normalized) | Captured | Required to fingerprint at all |
| Parameter **names** | Captured, normalized to placeholders | Needed for stable fingerprints |
| Parameter **values** | **Never captured** | User data |
| Connection string | **Never captured** | Credentials |
| Command duration and kind | Captured | Non-sensitive, needed for budgets |
| Stack traces | Off; when enabled, one per fingerprint | See ADR-0007 |
| Retained SQL samples | Bounded per fingerprint | Prevents unbounded retention |

Enforcement rules:

1. Redaction is applied by **one** policy, before any reporter sees a result. A reporter cannot
   opt out, and adding a reporter cannot introduce a leak.
2. String literals that survive normalization in the command text are redacted, so a query
   built with inline values does not become a data leak by accident.
3. Every new captured field requires a privacy review, a redaction test, and a documentation
   entry: enforced by the pull request template, not by memory.
4. `CaptureParameterValues` exists but defaults to `false`, and the documentation states
   plainly what enabling it means for anything you then share publicly.

## Rejected alternatives

**Capture values by default.** Better diagnostics, unacceptable default. A tool whose default
behavior can put customer data into a CI artifact does not deserve to be installed.

**Hash parameter values by default.** Superficially attractive: it distinguishes "51 different
values" from "51 identical values" without storing the data. Rejected for v0.1 on two grounds.
Hashes of low-cardinality values are trivially reversible, so the privacy claim would be weaker
than it looks and would need careful documentation to avoid misleading users. And it adds
hot-path cost for evidence quality that no user has asked for yet. This is the most likely
capability to be revisited: behind an explicit opt-in and with the reversibility caveat stated.

**No parameter handling at all.** Not viable: without normalizing parameter *names*,
provider-generated identifiers (`@p0`, `@__city_0`, `$1`) prevent equivalent queries from
sharing a fingerprint, which breaks the core feature.

## Consequences

- Some false-positive analysis has less context, so the false-positive issue form asks the
  reporter for the missing information instead.
- The redaction test matrix is a required check, and the ignored-findings path is covered too:
  an allowlisted finding is still redacted.
- Privacy defaults are documented in `SECURITY.md` and the package README, not buried in
  configuration reference material.

## Revisit when

A concrete user need arrives *and* a safe design exists: most plausibly opt-in parameter
value hashing with documented reversibility limits, or capturing only value *cardinality* per
fingerprint, which improves evidence without retaining any value at all.
