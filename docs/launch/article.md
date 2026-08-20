# Your endpoint returns 200 OK. It also runs 51 queries.

*Draft. Cross-post target: dev.to, a personal blog, or a GitHub Discussion. Update the version and the
links before publishing — see [README.md](./README.md).*

---

Here is a change that passes code review.

```csharp
var companies = await db.Companies.AsNoTracking().ToListAsync();

foreach (var company in companies)
{
    var departments = await db.Departments
        .AsNoTracking()
        .Where(department => department.CompanyId == company.Id)
        .ToListAsync();

    payload.Add(new { company.Id, company.Name, Departments = departments.Count });
}
```

It is correct. It returns the right data for all 50 companies. The status code is `200`. The response
body is byte-for-byte what the client expects. The integration test asserting that response passes.

It also executes 51 database queries — one for the list, then one per company.

Nothing about the response tells you that. Nothing about the status code tells you that. And no
conventional test tells you that either, because tests assert *what came back*, not *how many round trips
it took to produce it*. That asymmetry is why this class of regression reaches production so reliably: it
is invisible at exactly the moment someone is looking.

You find out later, from a latency graph, in an environment where the row count is not 50.

## The gap

There are good tools for this problem — on both sides of it.

**Before the fact**, EF Core's own logging and MiniProfiler will show you every query, while you are
looking at them. **After the fact**, an APM will tell you which endpoint got slow in production, once it
has.

What is missing is the middle: an assertion that runs when nobody is looking, in CI, and fails the build
when the number of queries an endpoint runs changes. Not "here is a list of queries, please read it" —
a red build.

Ruby has had this for years. [Bullet](https://github.com/flyerhzm/bullet) makes hidden query behaviour
visible during development and tests, and it is the reason Rails developers hit fewer of these. I wanted
the equivalent for EF Core, so I built [QueryGuard.NET](https://github.com/Benziza/queryguard-dotnet).

## What it looks like

The whole thing, in a test:

```csharp
await using var scope = QueryGuardScope.Start(
    "GET /api/companies",
    QueryGuardPolicy.Create("companies").WithMaxOccurrencesPerFingerprint(5),
    accessor: factory.SessionAccessor);

var response = await client.GetAsync("/api/companies");

var result = await scope.CompleteAsync();

QueryGuardAssert.Passes(result);
```

Against the loop above, that assertion fails, and this is what it prints:

```text
QueryGuard FAILED: GET /api/companies (policy 'companies')
  51 read queries in 2 distinct queries, 1.6 ms database time
  1 failures, 1 warnings, 0 ignored

Queries by frequency:
  QG-FP-FDB5F469  x50        0.6 ms  SELECT COUNT(*) FROM "Departments" AS "d" WHERE "d"."CompanyId" = ?
  QG-FP-EBC3AACB  x1         1.0 ms  SELECT "c"."Id", "c"."City", "c"."Name" FROM "Companies" AS "c"

Findings:
  [FAIL] max-occurrences-per-fingerprint: Fingerprint QG-FP-FDB5F469 executed 50 times; the budget is 5.
          Occurrences: 50 (budget: 5)
          First seen at command #2, last at command #51
          SQL: SELECT COUNT(*) FROM "Departments" AS "d" WHERE "d"."CompanyId" = ?
  [WARN] repeated-query: Potential N+1 pattern in GET /api/companies: fingerprint QG-FP-FDB5F469 executed 50 times.
          Repeated SQL is strong evidence, not proof of an application-level N+1 defect.
          Review eager loading, projection, or batching — or record an allowlist entry with a reason if the repetition is intentional.
```

Fix it with a projection and the same endpoint runs one query, returning identical data. There is a test
in the repository asserting the two payloads are equal, because a "fix" that returns something cheaper
and different is not a fix.

## How it works

Four steps, and the interesting decisions are all in step two.

**1. A session is the unit of measurement.** One HTTP request, or one test. `app.UseQueryGuard()` opens
one per request; `QueryGuardScope.Start(...)` opens one explicitly. No open session means no capture —
the tool stays silent rather than guessing which scope a command belongs to.

**2. An EF Core interceptor records commands into whichever session is active.** The interceptor is a
singleton and holds no state, because one instance sees commands from every concurrent request and every
`Task.Run` fan-out inside a single request. It asks an ambient accessor, backed by `AsyncLocal`, which
session it is currently inside.

**3. A fingerprint decides what "the same query" means.** To say "this ran 51 times" you have to decide
when two command texts are the same query, and raw text will not do — provider-generated parameter names
differ between executions. So the SQL is normalized, redacted, and hashed into something like
`QG-FP-FDB5F469`.

**4. Analysis happens when the scope closes**, not per command. Capture is one append; grouping and budget
evaluation happen once at the end.

## Three decisions worth arguing about

I would rather be argued with about these now than discover the answer from an issue in six months.

### It says "candidate", not "detected"

QueryGuard can prove exactly one thing: *the same normalized SQL executed N times in this scope.* It
cannot prove the application-level defect. Those are genuinely different sets in both directions.

Repeated SQL that is not a defect: three report sections each fetching a reference row; a deliberate poll;
a retry after a transient failure; per-tenant fan-out in a job that handles several tenants on purpose.

An N+1 defect that produces no repeated SQL: distinct parameterized shapes per iteration; a loop that
queries different entity types.

So every finding says *potential N+1* and *repeated-query candidate*. This is not hedging — it is the
difference between a tool people keep and a tool people mute. Say "N+1 detected" and be wrong once, and
the reader learns to ignore the next twenty findings, including the true ones.

For the same reason a repeated-query candidate is a **warning** by default. Turning it into a red build
out of the box would break the first CI run after installation, and a tool that does that gets switched
off rather than tuned. Failing requires configuring a budget, deliberately.

### It does not capture parameter values

By default QueryGuard does not capture parameter values or connection strings — there is no field for
them anywhere in the model, not merely a flag that filters them out. Literals surviving in the SQL are
replaced, retained samples are bounded, and redaction runs centrally before any reporter sees a string.

That last part is the design, not an implementation detail. A reporter that had to *remember* to redact
would eventually forget, and adding a reporter would become a way to introduce a leak. Because redaction
happens before a result object exists, no reporter — including one you write — can emit what was never
captured.

Capturing parameter values is available, because a query executed with 51 *different* keys is stronger
evidence than the same query 51 times. It is off by default because the report ends up in a CI artifact,
and sometimes in a public issue.

### Stack traces are off by default, and now there is a number

"Where is this query coming from?" is the first question anyone asks after seeing a finding, and the only
way to answer it from inside an interceptor is to capture a stack trace. So QueryGuard can — bounded to
one trace per fingerprint, with framework frames filtered out.

It is off by default. I assumed the cost was "small in absolute terms" and wrote that into the decision
record. Then I measured it:

| Distinct fingerprints | Off (default) | On, first occurrence only | Slower by | More allocation by |
| --- | --- | --- | --- | --- |
| 1 | 722 ns | 15,720 ns | 22× | 18× |
| 10 | 5,337 ns | 153,702 ns | 29× | 26× |

One trace per fingerprint costs 20–30× the entire rest of the capture path and allocates around 350 KB
across ten fingerprints. The assumption was wrong by more than an order of magnitude, and the decision
record now carries the table instead of the guess.

It also explains why there is no option to capture a trace per command: at ten commands per fingerprint
that is another order of magnitude, so that path does not exist in the API rather than existing as a trap.

For contrast, the number that matters most: **registered with no open scope, QueryGuard costs about
1.1 ns per command and allocates nothing.** That is one `AsyncLocal` read and a null check. It is what
makes "installing this does not change how your application behaves" a measurement rather than a promise.

Those are microbenchmarks — no database, no HTTP pipeline, three iterations, wide error bars. They tell
you the relative cost of QueryGuard's own choices, which is what the design rests on. They cannot tell
you what it costs in your application, and the benchmarks page says so at the top rather than in a
footnote.

## What it will not do

- **It will not prove an N+1.** See above. Some findings will be wrong, and the allowlist workflow exists
  for exactly that. Every allowlist entry requires a reason, because "turn this off" is not something a
  reviewer can evaluate and "bounded provider lookup, at most three report sections" is.
- **It only sees EF Core.** Dapper and raw ADO.NET commands are invisible, because it hooks EF Core's
  official `DbCommandInterceptor`.
- **No execution plans, no profiler UI, no hosted anything.** It counts queries and groups SQL.
- **It will not fix your code.** No automatic `Include`, no rewritten LINQ.
- **Two providers are actually tested**, SQLite and PostgreSQL, both in CI. SQL Server is verified against
  captured SQL fixtures with no live database. Everything else works through the official interception
  contract and is unverified for fingerprint *quality* — which is a different claim from working at all,
  and the provider page keeps them separate.
- **It is a preview.** The API will change. The report JSON carries an explicit `schemaVersion` so that
  breaking it is a visible event rather than a surprise for anyone building on the output.

## What I would like to know

Three specific things, because "any feedback welcome" gets none:

1. **Is the per-fingerprint budget the right primary guard?** `WithMaxOccurrencesPerFingerprint` catches
   an N+1 that a total-query-count budget misses entirely, so it is the rule I point people at first. Is
   that how you would reach for it, and is a default repeated-query threshold of 3 too noisy?

2. **Is fingerprint-based allowlisting too brittle?** An allowlist entry stops matching when the query
   changes, so the exception has to be justified again. I consider that the feature — it stops an
   allowlist quietly suppressing a query nobody recognises any more. In a real codebase, is it just
   annoying?

3. **Where does this belong?** Test suite only, or also as middleware in a development environment? Is
   there a case for running it in production that I am dismissing too quickly?

Answers, disagreements, and especially **false positives** are all more useful than a star. A repeated
query that QueryGuard flagged and was wrong about is the report that decides whether a tool like this is
worth keeping, and accepted reports become regression fixtures.

## Links

- Repository: <https://github.com/Benziza/queryguard-dotnet>
- Run the demo in three minutes: `git clone`, then `dotnet test samples/QueryGuard.SampleTests`
- Benchmarks, with raw output: [docs/benchmarks.md](../benchmarks.md)
- Provider support tiers: [docs/providers/README.md](../providers/README.md)
- When a finding is wrong: [docs/troubleshooting/false-positives.md](../troubleshooting/false-positives.md)
- MIT licensed.
