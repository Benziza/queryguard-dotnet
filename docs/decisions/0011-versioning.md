# ADR-0011: Preview-first SemVer, with the report schema versioned separately

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-043, QG-056, R-010

## Context

QueryGuard ships two contracts, and they change for different reasons:

1. The **.NET public API**: the types and members consumers compile against.
2. The **report schema**: JSON and JUnit output that CI pipelines and dashboards parse.

The second one is easy to forget and expensive to get wrong. Someone builds a dashboard on our
JSON, we rename a field in a patch release, and their pipeline breaks silently. A package version
alone does not tell a consumer whether the *output* they parse has changed.

There is also an honesty problem with releasing `1.0.0` early. A `1.0` says "this API is stable,
build on it". QueryGuard's API has had exactly one user: its author. Naming and shape need real
feedback before they are worth committing to.

## Decision

**Ship previews first under SemVer, and version the report schema independently of the package.**

Package versioning:

| Version | Meaning | API stability |
| --- | --- | --- |
| `0.1.0-preview.N` | Public installable preview | Experimental; breaking changes allowed with notes |
| `0.1.0` | First non-preview release | Stable within the `0.1` line |
| `0.2.0` | New integrations or detector capabilities | Minor evolution; migration guide required |
| `1.0.0` | Long-term API and report commitment | Not scheduled |

Rules:

- While below `1.0.0`, breaking changes are permitted, and every one appears in
  `CHANGELOG.md` with what changed, why, and how to migrate. "SemVer allows it" is not a
  substitute for telling people.
- `1.0.0` is not a date, it is a set of conditions: a stable API validated by real users,
  a compatibility policy, and a project that does not depend on a single maintainer. Releasing
  `1.0.0` to look mature would be the least mature thing available.
- Generated GitHub release notes list merged pull requests. They do not replace the curated
  changelog, which is where breaking, privacy-relevant, and schema changes live.

Report schema versioning:

- Every JSON report carries an explicit `schemaVersion`.
- Additive, backward-compatible fields bump the minor schema version.
- Removing or repurposing a field bumps the major schema version, and requires an ADR plus a
  changelog entry. It is a breaking change even in a preview.
- Schema shape is pinned by snapshot tests, so a change is impossible to make accidentally: it
  shows up as a diff in review.

## Rejected alternatives

**Release `0.1.0` stable immediately.** Skips the signal that the API is still moving, and makes
the first inevitable rename look like carelessness rather than the plan.

**Calendar versioning.** Communicates recency, communicates nothing about compatibility, which is
the only thing a version needs to communicate for a library.

**One version for both package and schema.** Simpler on the surface, and it means a consumer
parsing the JSON cannot tell whether a package upgrade affects them without reading the full
changelog. The extra number is cheap; the ambiguity is not.

## Consequences

- Preview users must expect churn, so the README and `SUPPORT.md` say so plainly and recommend
  pinning an exact version.
- The changelog is maintained per merge, not reconstructed at release time.
- Reporters must write `schemaVersion` and be covered by snapshot tests.
- Tag, package version, and release title must agree: verified by the release checklist. A
  published package version is never overwritten; a mistake becomes a new preview.

## Revisit when

The public API has been through real external use with no P0 API complaints, at which point
`0.1.0` stable is justified. `1.0.0` waits for the conditions above, however long that takes.
