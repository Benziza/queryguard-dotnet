---
title: API reference
description: The public API of every QueryGuard.NET package, generated from the source.
---

# API reference

Generated from the source and its XML documentation, so what you read here is what the compiler sees.

| Namespace | What lives there |
| --- | --- |
| `QueryGuard` | The core model: sessions, policies, results, findings, fingerprints, redaction, baselines |
| `QueryGuard.EntityFrameworkCore` | `UseQueryGuard`, the command interceptor, the session accessor |
| `QueryGuard.Testing` | `QueryGuardScope` and `QueryGuardAssert` — the two types most projects touch |
| `QueryGuard.Reporting` | Console, JSON, JUnit, and Markdown reporters, and the report reader |
| `QueryGuard.AspNetCore` | Per-request capture middleware for a running application |

Start from [`QueryGuardScope`](QueryGuard.Testing.QueryGuardScope.yml) and
[`QueryGuardAssert`](QueryGuard.Testing.QueryGuardAssert.yml) if you are writing a test, or
[`QueryGuardPolicy`](QueryGuard.QueryGuardPolicy.yml) to see every budget you can set.

> [!NOTE]
> Preview. The API will change before `1.0.0`, and the
> [versioning policy](../decisions/0011-versioning.md) says what that means in practice. The report JSON
> carries its own `schemaVersion` so a breaking change to the format is a visible event rather than a
> surprise.

`QueryGuard.Cli` is not listed: it is the `dotnet queryguard` tool, and every type in it is internal.
Its interface is its command line, documented under [baselines](../baselines/README.md).
