# ADR-0010: The testing API is framework-neutral; xUnit appears only in samples

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-038, QG-039, R-009

## Context

The main way QueryGuard is meant to be used is inside an integration test: open a scope, exercise
an endpoint, assert the query budget held. That makes the testing API the primary surface most
users touch.

The obvious implementation is to depend on xUnit and throw `Xunit.Sdk.XunitException`, so failures
render natively. But `QueryGuard.Testing` is a shipped package, and a package that references a
test framework forces that framework on everyone who installs it — including teams on NUnit,
MSTest, or TUnit. The alternative, shipping an adapter per framework, multiplies the surface area
of an unstable pre-1.0 API by four.

## Decision

**`QueryGuard.Testing` takes no test framework dependency. It throws a QueryGuard exception with a
message good enough that the framework's own reporting is enough.**

- `QueryGuardScope` opens and completes a named session, supporting both `IDisposable` and
  `IAsyncDisposable`, and returns the completed result.
- `QueryGuardAssert` throws `QueryGuardBudgetExceededException` — a plain exception type. Every
  test framework reports an unexpected exception with its message and stack, so no framework
  integration is required to get a usable failure.
- Because there is no framework-native formatting to lean on, the **message carries the evidence**:
  the policy name, expected versus actual totals, the top repeated fingerprint with its occurrence
  count, redacted SQL, ignored findings, and a link to the false-positive and allowlist guidance.
  A failure message a developer cannot act on without opening the docs is a bug.
- Output is bounded and redacted. A failing test must not dump an unbounded SQL wall into CI logs.
- The xUnit dependency exists only in this repository's own test and sample projects, where it is
  a development dependency and ships to nobody.

## Rejected alternatives

**Depend on xUnit directly.** Best failure rendering for xUnit users, and it makes the package
unusable-by-conscience for everyone else. Wrong trade for a library.

**Ship adapters for xUnit, NUnit, MSTest, and TUnit.** Four public surfaces to keep aligned while
the core API is explicitly unstable, each needing its own tests and its own release coordination.
This is the scope explosion the risk register warns about, and it buys ergonomics rather than
capability.

**Return a result and let users assert themselves.** Already supported — `QueryGuardScope` hands
back the completed result, and anyone can assert on it however they like. It is not sufficient as
the *only* option: the value of `QueryGuardAssert` is that the message is written once, well, by
the person who knows what evidence matters.

## Consequences

- Assertion ergonomics are slightly less idiomatic than a native framework assertion. Accepted,
  and mitigated by making the message excellent.
- The failure message is a tested artifact, not incidental output — it has snapshot coverage, so a
  change to it is visible in review.
- Because the result object is public and stable, a community adapter for any framework is a small
  wrapper. That is the intended path, once the core API has settled.

## Revisit when

The core API is stable and a community contributor wants to maintain a framework-native adapter.
It would live in a separate package with its own version, so its dependency never reaches
`QueryGuard.Testing` consumers.
