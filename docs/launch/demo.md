# Demo script — 90 seconds

*Draft. Re-run every command before recording; the numbers below are quoted in the article and the posts,
and a demo whose numbers do not match its write-up is worse than no demo.*

A terminal recording, no slides, no face, no music. The whole point is that the interesting thing is
invisible in the response and visible in one assertion — that reads better as plain text than as
production.

**Setup before recording:** clone into a fresh directory, run `dotnet build -c Release` once so the
recording is not 40 seconds of restore output, and widen the terminal to at least 110 columns so the SQL
lines do not wrap.

---

## 0:00 — 0:15 · The endpoint works

```bash
dotnet run --project samples/QueryGuard.SampleApi
```

In a second pane:

```bash
curl -s http://localhost:5000/api/companies | head -c 200
```

> "Fifty companies, department counts, two hundred OK. Correct data. Nothing wrong with this response."

Leave the JSON on screen for a beat. This is the part everyone recognises.

## 0:15 — 0:35 · The log disagrees

Scroll to the QueryGuard line in the server pane:

```text
warn: QueryGuard.AspNetCore.QueryGuardMiddleware[1000]
      QueryGuard GET /api/companies -> 200: 51 read queries in 2 groups, 0.9 ms database time, 1 failures, 2 warnings, 0 ignored.
```

> "Fifty-one queries. One for the list, fifty for the departments. The response never mentions it."

Highlight `51` and `200` in the same sentence. That contrast is the entire pitch.

## 0:35 — 0:50 · The fix, and proof it is the same data

```bash
curl -s http://localhost:5000/api/companies/projected | head -c 200
```

```text
info: QueryGuard.AspNetCore.QueryGuardMiddleware[1000]
      QueryGuard GET /api/companies/projected -> 200: 1 read queries in 1 groups, 0.1 ms database time, 0 failures, 0 warnings, 0 ignored.
```

> "Same payload, one query. There is a test in the repository asserting those two responses are identical,
> because a fix that returns something cheaper and different is not a fix."

## 0:50 — 1:20 · The part that belongs in CI

Stop the server. Run the demonstration suite:

```bash
dotnet test samples/QueryGuard.SampleTests
```

Then show the console report the failing-budget test prints:

```text
QueryGuard FAILED: GET /api/companies (policy 'companies')
  51 read queries in 2 distinct queries, 1.6 ms database time
  1 failures, 1 warnings, 0 ignored

Queries by frequency:
  QG-FP-FDB5F469  x50        0.6 ms  SELECT COUNT(*) FROM "Departments" AS "d" WHERE "d"."CompanyId" = ?
  QG-FP-EBC3AACB  x1         1.0 ms  SELECT "c"."Id", "c"."City", "c"."Name" FROM "Companies" AS "c"
```

> "This is the same information as a build failure instead of a log line. Which is the only version that
> catches it before merge."

## 1:20 — 1:30 · The caveat, said out loud

Scroll to the warning:

```text
Repeated SQL is strong evidence, not proof of an application-level N+1 defect.
```

> "It says candidate, not detected. It knows the same SQL ran fifty times; it cannot know whether that is
> a bug. Sometimes it is not — and an intentional repetition gets an allowlist entry with a written
> reason, which stays visible in the report instead of disappearing."

End on the repository URL. No call to action beyond the link.

---

## What not to do in this recording

- **No speed comparison against another tool.** Different question, different part of the lifecycle.
- **No production latency claim.** The benchmarks are microbenchmarks and the page says so.
- **Do not hide the warning.** It is the most credible thing on screen.
- **Do not edit the numbers to be rounder.** 51 is more believable than 50 precisely because nobody would
  choose it.
