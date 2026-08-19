# ADR-0003: Report "potential N+1 / repeated-query candidate", never "N+1 detected"

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-024, QG-033, R-001

## Context

QueryGuard's headline capability is spotting the pattern where one query per parent row is
executed inside a loop — the N+1 problem. The temptation is to call it that, plainly, because
"detects N+1 queries" is a far better marketing line than "reports repeated-query candidates".

But look at what QueryGuard actually observes: a sequence of SQL command texts, their
durations, and the scope they ran in. From that it can prove exactly one thing — *the same
normalized SQL executed N times in this scope*.

It cannot prove the application-level defect, because repeated SQL and an N+1 bug are not the
same set:

- **Repeated SQL that is not a defect.** A bounded lookup over three report sections. A
  deliberate per-tenant query in a fan-out. A retry. A cache warm-up. A paged sweep whose
  page count happens to be high.
- **An N+1 defect that produces no repeated SQL.** Distinct parameterized shapes per
  iteration, provider SQL that varies enough to fingerprint differently, or a loop that
  queries different entity types.

A tool that says "N+1 detected" and is wrong once teaches the user that its output is
unreliable. After that, every real finding gets ignored — or the tool gets switched off.
Over-claiming does not just risk being wrong; it destroys the value of being right.

## Decision

**QueryGuard reports evidence, in words that match the evidence.**

- The finding type is a **repeated-query candidate**, described as a **potential** N+1 pattern.
- Every finding carries its evidence: fingerprint ID, occurrence count, total database time,
  first and last sequence numbers, and a bounded sample of the normalized SQL.
- Remediation is offered as *strategies to review* — eager loading, projection, batching, or a
  documented allowlist entry — never as a fix QueryGuard claims to have identified.
- Documentation and marketing use the same words as the code. "Automatically fixes every N+1
  query", "guaranteed N+1 detection", and "perfect N+1 detection" are banned phrases, and the
  false-positive issue form exists precisely because we expect to be wrong sometimes.
- A false-positive report is treated as a first-class bug, not a support question. Accepted
  reports become regression fixtures.

## Rejected alternatives

**Claim definitive N+1 detection.** Better positioning, dishonest, and self-defeating for the
reasons above.

**Call it only "duplicate query" and avoid N+1 entirely.** Technically safe, but it hides the
problem the user actually has. Developers search for "N+1", think in terms of N+1, and need to
know this tool is relevant to it. The honest framing is to name the problem and be precise
about the strength of the evidence — not to refuse to name it.

## Consequences

- Findings are wordier than a bare assertion would be. Accepted: the evidence *is* the product.
- Some users will ask for stronger wording. The answer is to point at this record.
- The allowlist mechanism is not optional. If we tell users some findings will be wrong, we
  must give them a way to record that — with a required reason, and with the ignored finding
  still visible in the report so nothing is silently dropped.
- Comparisons against profilers and APM tools stay in "different job" territory rather than
  claiming superiority.

## Revisit when

A detector exists that can produce real semantic evidence of an N+1 — for example by
correlating command execution with the materialization of a parent result set, or with
call-site identity — and it has been validated against the false-positive fixture suite.
Stronger wording requires stronger evidence, in that order.
