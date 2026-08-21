# Architecture Decision Records

Every decision here was expensive to reverse once code depended on it, so it was written
down before or alongside the implementation rather than reconstructed afterwards.

Each record states the decision, what was rejected, why, what it costs, and, most
importantly, the **trigger** that should make us revisit it. A decision without a revisit
trigger is a belief, not an engineering choice.

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](./0001-public-personal-repo.md) | Repository visibility and ownership | Accepted |
| [0002](./0002-session-propagation.md) | Session propagation model | Accepted |
| [0003](./0003-detector-terminology.md) | Detector terminology | Accepted |
| [0004](./0004-parameter-privacy.md) | Parameter capture and privacy defaults | Accepted |
| [0005](./0005-sql-fingerprints.md) | SQL fingerprint strategy | Accepted |
| [0006](./0006-aspnet-observe-only.md) | ASP.NET Core behavior | Accepted |
| [0007](./0007-stack-trace-policy.md) | Stack trace capture | Accepted |
| [0008](./0008-target-frameworks.md) | Supported .NET versions | Accepted |
| [0009](./0009-provider-matrix.md) | Provider support | Accepted |
| [0010](./0010-testing-api.md) | Testing framework dependency | Accepted |
| [0011](./0011-versioning.md) | Versioning and report schema | Accepted |
| [0012](./0012-trusted-publishing.md) | Publishing credentials | Accepted |
| [0013](./0013-baseline-storage.md) | Baseline storage and comparison | Accepted |

## Statuses

- **Proposed**: written, not yet acted on.
- **Accepted**: in force. Code and documentation must match it.
- **Superseded**: replaced by a later ADR, which is linked from the header.

## Adding a record

Open a pull request that adds `NNNN-short-kebab-title.md` using the same headings as an
existing record. A change to a public API, a captured field, the report schema, the
supported framework matrix, or the release pipeline needs an ADR before the implementation.
