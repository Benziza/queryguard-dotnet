# ADR-0006: The ASP.NET Core integration observes and never alters the response

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-035, QG-036, QG-037, R-007

## Context

Bullet, the Rails library that inspired QueryGuard's product idea, is memorable partly because
it puts warnings where developers cannot miss them: a footer in the page, a JavaScript alert, a
browser console message. That immediacy is genuinely valuable.

Translating it directly to ASP.NET Core means injecting into responses or adding headers. And
QueryGuard is middleware in the same pipeline as the application: whatever it does to the
response, the application experiences.

The risk is precise. A diagnostics tool that changes observed behavior is worse than no tool,
because it makes every subsequent test result suspect: "does it fail because of the bug, or
because QueryGuard is enabled?" Response mutation is also unsafe in practice for streamed
responses, already-started responses, and content types that are not documents at all.

## Decision

**The middleware observes. It does not touch the response.**

- A session is opened around each configured request and finalized in a `finally`, so the
  success path and the exception path both produce a result.
- Output goes to `ILogger` as a structured summary plus findings, with stable event IDs. Nothing
  is written to the response body, and no headers are added.
- The original response and the original exception pass through untouched. QueryGuard never
  swallows, wraps, replaces, or reorders an application exception: a reporter failing must not
  be able to mask an application failure.
- The middleware never throws on the request path. Only the explicit testing API
  (see [ADR-0010](./0010-testing-api.md)) turns a budget verdict into an exception, because in
  a test that *is* the intended outcome.
- Policies are selected by route **pattern** (`GET /api/companies/{id}`), not by the resolved
  URL, so `/api/companies/1` and `/api/companies/2` share one policy instead of creating a new
  one per identifier.
- The intended default deployment is development and test environments. Enabling it in
  production is a documented, deliberate choice, not the default.

## Rejected alternatives

**Response body injection (a Bullet-style footer).** Highest visibility, unacceptable risk. It
mutates application output, breaks on streaming and non-HTML responses, and needs content-type
sniffing that will eventually be wrong.

**Response headers by default.** Much safer than body injection and still rejected as a
*default*: headers are part of the response contract, they can leak diagnostics to clients if
the tool is accidentally enabled outside development, and they can break a test that asserts on
headers. This is the most defensible thing to add later, behind an explicit opt-in.

**Throwing from the middleware when a budget fails.** Turns a diagnostic into an outage. The
place to fail is a test.

## Consequences

- QueryGuard is less immediately visible than a profiler UI. Logs, JSON, and JUnit output are
  the primary channels, and the sample plus README have to carry the discoverability that a
  footer would have provided for free.
- The middleware needs integration tests proving equivalence with QueryGuard enabled and
  disabled: same status, same body, same exception type and stack.
- Streaming and long-lived responses need explicit test coverage, because "finalize the session
  in `finally`" has to hold when the response is still open.

## Revisit when

There is real demand for opt-in development-only response headers: for example from a team
whose local workflow cannot easily read structured logs. It would ship disabled, be documented
as development-only, and be covered by a test asserting it is inert by default.
