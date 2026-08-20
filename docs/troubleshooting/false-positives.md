# When a finding is wrong

QueryGuard can prove exactly one thing: **the same normalized SQL executed N times in this scope.** It
cannot prove the application-level defect. So some findings will be wrong, and this page is about what to
do when one is.

If you are here because a finding looks wrong, you are using the tool correctly. Please
[tell us about it](https://github.com/Benziza/queryguard-dotnet/issues/new?template=false_positive.yml) —
accepted reports become regression fixtures, which is how the defaults get better for everyone.

## Why the wording matters

Findings say *potential N+1* and *repeated-query candidate*, never "N+1 detected". That is not hedging;
repeated SQL and an N+1 defect are genuinely different sets:

**Repeated SQL that is not a defect**

- A bounded lookup — three report sections, each fetching a reference row.
- A deliberate poll on a fixed interval.
- A retry after a transient failure.
- Per-tenant fan-out in a job that handles several tenants on purpose.
- A paged sweep whose page count happens to be high.

**An N+1 defect that produces no repeated SQL**

- Distinct parameterized shapes per iteration.
- Provider SQL that varies enough to fingerprint differently.
- A loop that queries different entity types.

A tool that says "N+1 detected" and is wrong once teaches you to ignore every later finding. See
[ADR-0003](../decisions/0003-detector-terminology.md).

## Four ways to respond

In order of preference. The first is best because it is the most specific and the most reviewable.

### 1. Document the exception on the query

Best when the repetition is intentional *by design* and belongs to one call site. The declaration lives
next to the code that needs it, so it moves with the query and is visible to anyone editing it.

```csharp
var sections = await db.ReportSections
    .TagWith("QueryGuard:Ignore reason=three-report-sections-bounded-by-layout")
    .Where(section => section.ReportId == reportId)
    .ToListAsync();
```

The finding is still reported, marked **ignored**, with your reason attached. It stops affecting the
outcome; it does not disappear.

### 2. Allowlist the fingerprint on the policy

Best when the exception belongs to an endpoint rather than to a single LINQ expression, or when you cannot
edit the query. Run it once, read the fingerprint out of the report, then record why:

```csharp
options.ForEndpoint("GET /api/reports/{id}", policy => policy
    .AllowFingerprint(
        "QG-FP-1A2B3C4D",
        reason: "Bounded provider lookup; at most three report sections."));
```

Matching by fingerprint is deliberately brittle: if the query changes, its fingerprint changes and the
entry stops matching, so the exception has to be justified again. That is what stops an allowlist from
quietly suppressing a query nobody recognizes any more.

### 3. Allowlist by tag

Best when the same intentional pattern appears in several places. Survives a query changing, which is
right for a pattern that is intentional by design rather than by accident.

```csharp
policy.AllowQueryTag("bounded-reference-lookup", reason: "Capped by layout, not by row count.");
```

### 4. Raise the threshold

Best when a whole endpoint legitimately repeats more than the default three times, and you do not want to
enumerate individual queries.

```csharp
options.ForEndpoint("GET /api/reports/{id}", policy => policy.WithRepeatedQueryThreshold(6));
```

The trade-off: it loosens the guard for *every* query in that endpoint, not just the one you had in mind.
Prefer the first three options when you can.

## The reason is not optional

Every allowlist mechanism requires reason text, and that is the whole design. "Turn this off" is not
something a reviewer can evaluate. "Bounded provider lookup; at most three report sections" is — and it
appears in a pull request diff where they can check it.

A reason also reaches reports. If a report is shared, so is the reason. Write it for a reader who does
not have your context.

## What not to do

**Do not look for a global off switch.** There is not one, deliberately. Suppressing everything is
indistinguishable from uninstalling the tool, and it is a decision that gets made once in frustration and
then never revisited.

**Do not remove the interceptor to quiet a report.** You lose every other guard on the way. Allowlist the
specific finding instead.

**Do not raise a budget to make a red build green** without deciding whether the budget or the code was
wrong. Budgets exist so the answer is a decision rather than a default.

## Ignored findings stay visible

An allowlisted finding appears in every output — console, JSON, JUnit, logs — marked ignored, with its
reason. That is not an oversight. An allowlist that hides findings becomes the place real problems go to
die: someone silences a query in March, the code changes in June, and nobody ever looks again.

`IgnoredFindingCount` on the result makes it easy to notice when a scope is carrying more exceptions than
anyone remembers granting.

## Reporting one

Use the
[false-positive form](https://github.com/Benziza/queryguard-dotnet/issues/new?template=false_positive.yml).
It asks for the fingerprint, occurrence count, redacted normalized SQL, and — most importantly — *why the
repetition is intentional*. That last part is what turns a report into a fixture.

Use synthetic or fully redacted SQL. Do not paste production schema names, parameter values, or customer
data into a public issue.
