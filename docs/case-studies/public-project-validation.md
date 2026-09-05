# Public project validation

QueryGuard `0.1.0-preview.6` was tested in local copies of three public ASP.NET Core projects.
These checks do not imply adoption or support from their maintainers.

## Method

For each pinned commit below, one existing integration test setup was extended to:

1. Install `QueryGuard.AspNetCore.Testing` from nuget.org.
2. Measure one request with `TrackQueries`.
3. Check the exact count and allow at most one occurrence per query ID.

## Results

| Project and commit | Framework and provider | Request | QueryGuard result |
| --- | --- | --- | --- |
| [jasontaylordev/CleanArchitecture at `10f1a45`](https://github.com/jasontaylordev/CleanArchitecture/tree/10f1a45df0d86bb87b083f3a0e249d755093fbbd) | NUnit, SQLite | `POST /api/Users/register` | 1 read query, 1 group, 0 findings |
| [SSWConsulting/SSW.VerticalSliceArchitecture at `b3926fe`](https://github.com/SSWConsulting/SSW.VerticalSliceArchitecture/tree/b3926fe461fa79fd81e163d851f1dec00a5ba84e) | xUnit, SQL Server Testcontainers | `GET /api/heroes` | 2 read queries, 2 groups, 0 findings |
| [alex289/CleanArchitecture at `70a13e3`](https://github.com/alex289/CleanArchitecture/tree/70a13e310abf8742b938a80dff48ae0735f6b5ef) | NUnit, SQL Server Testcontainers | `GET /api/v1/Tenant/{id}` | 2 read queries, 2 groups, 0 findings |

No false positive appeared in these three requests.

## What the work found

- **SSW:** `preview.5` required EF Core `10.0.11`, but the project used `10.0.10`.
  [PR #127](https://github.com/Benziza/queryguard-dotnet/pull/127) lowered the dependency floor.
  The published `preview.6` packages restored and passed.
- **alex289:** the request reads a tenant and its users in two separate queries.
  A total budget of two and a per-query limit of one matched this behavior.
- **Jason Taylor:** the local check used SDK `10.0.302` instead of the requested `10.0.400`.
  No QueryGuard change was needed.

## Limits

Only one request per project was tested. This does not measure performance or long-running behavior.
The changes stayed in local checkouts; no pull requests were sent to those projects.
Existing project warnings were outside the QueryGuard results.