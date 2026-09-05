---
title: API reference
description: QueryGuard.NET types and methods, generated from source.
---

# API reference

Browse types and methods generated from the source documentation.

| Namespace | Includes |
| --- | --- |
| `QueryGuard` | Sessions, policies, results, fingerprints, redaction, and baselines |
| `QueryGuard.EntityFrameworkCore` | EF Core registration and command capture |
| `QueryGuard.Testing` | Test scopes and assertions |
| `QueryGuard.Reporting` | Report writers and readers |
| `QueryGuard.AspNetCore` | Request middleware |

Start with [`QueryGuardScope`](QueryGuard.Testing.QueryGuardScope.yml),
[`QueryGuardAssert`](QueryGuard.Testing.QueryGuardAssert.yml), or
[`QueryGuardPolicy`](QueryGuard.QueryGuardPolicy.yml).

For HTTP integration tests, see the [testing guide](../testing/README.md).
For CLI commands, see [baselines](../baselines/README.md).

API and report compatibility follow the [versioning policy](../decisions/0011-versioning.md).
JSON reports include a `schemaVersion` field.