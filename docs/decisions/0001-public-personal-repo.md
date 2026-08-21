# ADR-0001: Develop in public under a personal account

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-001, [ADR-0004](./0004-parameter-privacy.md)

## Context

QueryGuard.NET is a developer tool. Its value depends on people finding it, trusting it, and
telling us when its detector is wrong. None of that happens in a private repository.

Three options were on the table:

1. Keep it private until a polished `v1`, then publish.
2. Publish immediately under a new GitHub organization.
3. Publish immediately under a personal account.

The project also carries a specific risk: it was conceived while working on EF Core
performance problems professionally. Nothing from that work, including code, SQL, schema names,
architecture, or tickets, may appear here.

## Decision

**Develop in public from day one, under the personal `Benziza` account.**

A short local bootstrap (measured in hours, not weeks) precedes the first push, and its only
purpose is to clear the pre-public checklist: no employer intellectual property, no secrets
in history, names still available, MIT license in place, privacy defaults set, security
contact documented, samples synthetic, README honest.

Publishing is **not** gated on the code being finished.

## Rejected alternatives

**Private until v1.** The failure mode is not embarrassment, it is irrelevance: a detector
that has never been challenged by an outside user ships with defaults tuned to one person's
imagination. Repeated-SQL detection is exactly the kind of feature that needs adversarial
input early, when changing the defaults is still cheap. Polish is also a poor reason to stay
private: a public repository with visible tests, CI, and honest limitations reads as more
credible than a private one, not less.

**A GitHub organization immediately.** An organization solves governance problems such as
multiple maintainers, team-based review routing, and shared ownership. QueryGuard has none of
those problems yet. What it does have is a need for the work to be attributable, and a
personal account does that better.

## Consequences

- Public security hygiene is mandatory from the first commit: secret scanning, push
  protection, Dependabot, CodeQL, and least-privilege workflow tokens.
- Development history is permanent and public, including mistakes. Commits and pull requests
  are written for an audience.
- Required approvals on `main` are set to zero, because a solo maintainer cannot provide an
  independent review. Pull requests are still mandatory, and the self-review procedure in
  `CONTRIBUTING.md` replaces the missing reviewer. This is the one governance compromise
  made here, and it is temporary.
- `CODEOWNERS` splits paths ahead of time so adding a second maintainer is a one-line change.

## Revisit when

- A second trusted maintainer joins, at which point required approvals go to one and
  code-owner review is enabled.
- Governance stops fitting a personal account: a funded roadmap, a foundation, or a team
  taking shared ownership. Then, and only then, transfer to an organization.
- Any question arises about employer intellectual property in the repository. In that case
  work stops until it is resolved, ahead of every other priority.
