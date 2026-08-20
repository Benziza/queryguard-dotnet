# Community posts

*Drafts. Each one ends in a specific question. None of them asks for stars — a post that asks for
attention gets attention, and a post that asks a real question gets answers you can act on.*

All three link to the same feedback thread, so responses do not scatter across four sites:
[Discussion #80](https://github.com/Benziza/queryguard-dotnet/discussions/80).

Confirm `0.1.0-preview.1` is actually on nuget.org before posting anywhere. The first thing a reader does
is copy the install command, and a post whose first instruction fails is a post that gets one comment.

---

## r/dotnet

**Title:** I got tired of N+1 regressions passing code review, so I made query counts assertable

Body:

> An endpoint that returns `200 OK` with correct data and runs 51 queries to do it looks identical, from
> the outside, to one that runs 1. The response is right, the status is right, and the integration test
> asserting that response passes. Tests assert what came back, not how many round trips produced it.
>
> So I built QueryGuard.NET — an EF Core interceptor that records commands inside a request or a test,
> groups repeated SQL into fingerprints, and fails the build when a budget you set is exceeded.
>
> ```csharp
> await using var scope = QueryGuardScope.Start(
>     "GET /api/companies",
>     QueryGuardPolicy.Create("companies").WithMaxOccurrencesPerFingerprint(5));
>
> var response = await client.GetAsync("/api/companies");
>
> QueryGuardAssert.Passes(await scope.CompleteAsync());
> ```
>
> Deliberate limits, up front: it says *repeated-query candidate*, never "N+1 detected", because all it
> can prove is that the same normalized SQL ran N times — some repetition is correct. It does not capture
> parameter values by default. It only sees EF Core, not Dapper or raw ADO.NET. It is a preview and the
> API will change.
>
> Two providers are actually integration-tested in CI, SQLite and PostgreSQL. Everything else works
> through the official interception contract but its fingerprint *quality* is unverified, which is a
> different claim and the repository keeps them separate.
>
> The repository has a runnable sample: `dotnet test samples/QueryGuard.SampleTests` shows a passing test
> about a failing budget, printing what a developer would actually see.
>
> **The question I would most like answered:** is a per-fingerprint occurrence budget the right primary
> guard? It catches an N+1 that a total-query-count budget misses entirely, so it is what I point people
> at first — but I would like to know whether that is how you would reach for it, and whether a default
> repeated-query threshold of 3 is too noisy in a real codebase.
>
> Repository: <https://github.com/Benziza/queryguard-dotnet> · MIT · design questions in
> [this thread](https://github.com/Benziza/queryguard-dotnet/discussions/80)

---

## Hacker News (Show HN)

**Title:** Show HN: QueryGuard.NET – make EF Core query counts assertable in CI

Body:

> The regression I built this for survives code review by design: the endpoint returns the correct data
> with a 200, and it takes 51 queries instead of 1 to do it. Nothing in the response says so, and the test
> asserting that response passes.
>
> QueryGuard records EF Core commands inside a request or a test, normalizes and hashes the SQL so
> repeated queries group together, and fails the build when a budget is exceeded. The interesting design
> constraints turned out to be about credibility rather than mechanism:
>
> - It reports "candidate", not "detected". Repeated SQL and an N+1 defect are different sets in both
>   directions. Claim detection, be wrong once, and the reader mutes the next twenty findings.
> - Repeated-query findings are warnings by default. Failing a first CI run after installation is how a
>   tool gets switched off rather than tuned.
> - Redaction happens before a result object exists, so a reporter cannot leak what was never captured —
>   including a reporter someone else writes.
> - Stack-trace capture is off by default. I had written "the cost is small in absolute terms" into the
>   decision record, then measured it: 22–30× the rest of the capture path, ~350 KB across ten
>   fingerprints. The record now carries the table instead of the guess.
>
> With no scope open it costs ~1.1 ns per command and allocates nothing, which is what makes "installing
> this does not change your application's behaviour" a measurement. Those are microbenchmarks with three
> iterations and wide error bars, and the benchmarks page leads with that rather than burying it.
>
> Limits: EF Core only, two providers integration-tested, no execution plans, no UI, preview API.
>
> <https://github.com/Benziza/queryguard-dotnet>
>
> **What I would like to hear about:** whether fingerprint-based allowlisting is too brittle in practice.
> An entry stops matching when the query changes, so the exception has to be justified again. I think
> that is the feature — it stops an allowlist silently suppressing a query nobody recognises any more —
> but I have not run it in a large codebase for a year.
>
> That question and two others are in
> [a feedback thread](https://github.com/Benziza/queryguard-dotnet/discussions/80) if a comment here is
> the wrong place for it.

---

## LinkedIn / X

Short, one idea, no thread.

> Your endpoint returns 200 OK.
>
> It also ran the same query 51 times.
>
> The response is correct. The status is correct. The integration test passes. Tests assert what came
> back, not how many round trips produced it — which is why this kind of regression reaches production so
> reliably.
>
> QueryGuard.NET makes the query count assertable: record EF Core commands in a request or a test, group
> repeated SQL, fail the build when a budget is exceeded.
>
> It says "candidate", not "detected" — all it can prove is that the same SQL ran N times, and sometimes
> that is correct. Every intentional exception is recorded with a written reason instead of a mute button.
>
> Preview, MIT, EF Core only, two providers tested in CI.
>
> I am specifically looking for false positives — a repeated query it flagged that was actually fine.
> That is the report that decides whether a tool like this is worth keeping.
>
> <https://github.com/Benziza/queryguard-dotnet>

---

## Where feedback goes

One thread, linked from all three: <https://github.com/Benziza/queryguard-dotnet/discussions/80>.

It asks the three questions from the article — the primary guard, allowlist brittleness, and where this
belongs in the lifecycle — and nothing else. A thread that asks three things gets answers to three
things; a thread that asks for "thoughts" gets a star and no information.
