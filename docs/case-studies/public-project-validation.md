# Public project validation

QueryGuard `0.1.0-preview.6` was tested in three public ASP.NET Core projects before the stable
`0.1.0` release.

This is a compatibility check, not an adoption claim. The test changes stayed in local checkouts. No
pull requests were opened in the other projects because their maintainers did not request these
changes.

## Method

For each project, the validation used an existing integration test setup:

1. Check out the exact commit listed below.
2. Install `QueryGuard.AspNetCore.Testing` from nuget.org.
3. Add one `WebApplicationFactory` request test with `TrackQueries`.
4. Set a total query budget and allow only one occurrence per fingerprint.
5. Assert the exact query count and record the result.

The final run used the published `0.1.0-preview.6` packages, not a local package feed.

## Results

| Project and commit | Framework and provider | Request | QueryGuard result |
| --- | --- | --- | --- |
| [jasontaylordev/CleanArchitecture at `10f1a45`](https://github.com/jasontaylordev/CleanArchitecture/tree/10f1a45df0d86bb87b083f3a0e249d755093fbbd) | NUnit, SQLite | `POST /api/Users/register` | 1 read query, 1 group, 0 findings |
| [SSWConsulting/SSW.VerticalSliceArchitecture at `b3926fe`](https://github.com/SSWConsulting/SSW.VerticalSliceArchitecture/tree/b3926fe461fa79fd81e163d851f1dec00a5ba84e) | xUnit, SQL Server Testcontainers | `GET /api/heroes` | 2 read queries, 2 groups, 0 findings |
| [alex289/CleanArchitecture at `70a13e3`](https://github.com/alex289/CleanArchitecture/tree/70a13e310abf8742b938a80dff48ae0735f6b5ef) | NUnit, SQL Server Testcontainers | `GET /api/v1/Tenant/{id}` | 2 read queries, 2 groups, 0 findings |

The request setup worked with both NUnit and xUnit, and with SQLite and SQL Server. No QueryGuard
false positive appeared in these three requests.

## What the work found

The first SSW restore failed before the test could run. `0.1.0-preview.5` required EF Core `10.0.11`,
while the project pinned `10.0.10`. QueryGuard did not need that exact patch. [Issue #126](https://github.com/Benziza/queryguard-dotnet/issues/126)
tracked the problem, and [pull request #127](https://github.com/Benziza/queryguard-dotnet/pull/127)
lowered the package dependency floors. The same project restored and passed with `0.1.0-preview.6`.

The alex289 request first used a total budget of one and failed with two different queries. The
endpoint reads the tenant and its users separately. Raising the total budget to two while keeping the
per-fingerprint limit at one represented the endpoint correctly and still guarded against repetition.

The Jason Taylor checkout requested .NET SDK `10.0.400`, which was not installed on the validation
machine. The checkout used the installed `10.0.302` feature band for this test. No QueryGuard change
was needed.

## Limits

- Only one request was tested in each project.
- This does not measure performance or long-running application behavior.
- Existing warnings from the projects were outside QueryGuard and were not treated as product results.
- These local validation patches are not support commitments from the other maintainers.

The validation found one packaging problem, verified its fix from nuget.org, and found no blocker for
the stable `0.1.0` API.
