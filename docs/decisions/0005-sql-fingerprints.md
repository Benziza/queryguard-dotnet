# ADR-0005: Conservative text normalization plus a stable hash, not a SQL parser

- **Status:** Accepted
- **Date:** 2026-08-19
- **Deciders:** Mohamed Benziza
- **Related:** QG-019, QG-020, QG-021, QG-022, R-004, R-005

## Context

To say "this query ran 51 times", QueryGuard must decide when two command texts are *the same
query*. EF Core does not hand us a stable identifier, and raw command text is not usable
directly: provider-generated parameter names differ between executions, and formatting differs
between providers and EF versions.

Three strategies:

1. Parse SQL into an abstract syntax tree per dialect and compare structurally.
2. Compare exact command text.
3. Normalize the text conservatively, then hash it.

There are two opposite failure modes, and they are not symmetric:

- **Over-normalizing** merges genuinely different queries into one group. The report becomes
  actively misleading — it points at the wrong SQL. This is the worse failure.
- **Under-normalizing** splits one logical query into several groups, so a real repeated-query
  pattern goes unreported. Bad, but the tool is merely silent rather than wrong.

## Decision

**Normalize the command text conservatively, then hash the normalized text into a short stable
ID. When in doubt, do less.**

Normalization steps, all order-preserving:

1. Normalize line endings and collapse runs of whitespace.
2. Strip non-semantic comments, while **preserving** the recognized `QueryGuard:` tag so
   `TagWith` based ignore hints survive.
3. Map provider parameter references and declarations to a single placeholder form, covering
   the `@p0` / `@__city_0` / `$1` / `?` families.
4. Redact surviving string and numeric literals, which serves both fingerprint stability and
   the privacy contract in [ADR-0004](./0004-parameter-privacy.md).
5. Hash the result and render a short, readable ID: `QG-FP-1A2B3C4D`.

Hard constraints:

- **Never reorder tokens.** No clause sorting, no alias canonicalization, no attempt to
  recognize that two differently written queries are semantically equivalent.
- Unrecognized input degrades to hashing the whitespace-normalized text. Unknown SQL produces
  a *less useful* fingerprint, never a wrong one.
- The ID must be identical across runs, processes, and both target frameworks — so the hash is
  an explicit stable algorithm, never `string.GetHashCode()`, which is randomized per process.
- The normalized text is retained alongside the ID, because a fingerprint the user cannot read
  is not evidence.

Every normalization rule is pinned by approval-style fixtures built from real generated SQL for
SQLite, PostgreSQL, and SQL Server. Changing a rule changes a fixture, visibly, in a diff.

## Rejected alternatives

**Full SQL parsing.** The correct answer to a different, much larger problem. A parser per
dialect, kept current with each provider's SQL generation, is a project on its own — and every
gap in it becomes a wrong grouping in QueryGuard. Rejected as out of scope, permanently for
v0.1 and probably beyond.

**Exact command text.** Zero risk of merging different queries, and useless: provider-generated
parameter names alone would split a repeated query into N distinct groups, which is exactly the
case QueryGuard exists to find.

## Consequences

- Fingerprint quality varies by provider. The support matrix says which providers are verified
  and which are best-effort — see [ADR-0009](./0009-provider-matrix.md).
- Both failure modes need a public reporting path: the false-positive form for over-grouping,
  the provider form for under-grouping.
- Normalization runs on the hot path, so it is measured by a benchmark on representative short
  and long SQL rather than assumed to be cheap.
- If provider-specific behavior turns out to need real divergence, the normalizer is behind an
  interface, so a provider-specific strategy can be added without touching the detector.

## Revisit when

- Provider evidence shows the generic normalizer systematically fails to group equivalent SQL
  for a provider people actually use. The response is a provider-specific normalizer behind the
  existing interface, not a parser.
- The normalizer shows up as a significant cost in the benchmark for long generated SQL.
