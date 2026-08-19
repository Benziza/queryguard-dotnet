# QueryGuard.Core

Core contracts for [QueryGuard.NET](https://github.com/Benziza/queryguard-dotnet): the
session model, captured command records, SQL fingerprints, query budgets, policies, and
findings.

You normally do not install this package directly. Install the integration you need and
`QueryGuard.Core` comes with it:

| Package | Use it for |
| --- | --- |
| `QueryGuard.EntityFrameworkCore` | Capturing EF Core relational commands |
| `QueryGuard.AspNetCore` | Request-scoped sessions and per-endpoint policies |
| `QueryGuard.Testing` | Explicit scopes and budget assertions in integration tests |
| `QueryGuard.Reporting` | Console/logger, JSON, and JUnit XML reports |

## Privacy defaults

QueryGuard does not capture parameter values or connection strings, does not collect stack
traces unless explicitly enabled, and bounds retained samples per fingerprint. Redaction is
applied centrally before any reporter writes output.

## Preview

Public APIs and the report schema may change before `1.0.0`. See the
[changelog](https://github.com/Benziza/queryguard-dotnet/blob/main/CHANGELOG.md).
