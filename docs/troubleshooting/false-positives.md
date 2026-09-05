# When a finding is wrong

Repeated SQL is not always an N+1 defect. Bounded lookups, retries, polling, and paged jobs
can repeat queries on purpose. QueryGuard can also miss an N+1 if each query has a different shape.

Review the count and SQL before changing a budget.

## Four ways to respond

### 1. Document the exception on the query

Use this when the exception belongs to one call site:

```csharp
var sections = await db.ReportSections
    .TagWith("QueryGuard:Ignore reason=three-report-sections-bounded-by-layout")
    .Where(section => section.ReportId == reportId)
    .ToListAsync();
```

The finding stays in reports as **ignored**, with the reason attached.

### 2. Allowlist the fingerprint on the policy

Use this when you cannot edit the query or the exception belongs to an endpoint.
Copy the ID from a report:

```csharp
options.ForEndpoint("GET /api/reports/{id}", policy => policy
    .AllowFingerprint(
        "QG-FP-1A2B3C4D",
        reason: "Bounded provider lookup; at most three report sections."));
```

If the query's ID changes, the entry stops matching. Review the exception again.

### 3. Allowlist by tag

Use a tag for the same intentional pattern across several queries:

```csharp
policy = policy.AllowQueryTag("bounded-reference-lookup", reason: "Capped by layout, not by row count.");
```

This keeps matching the tag even when the SQL changes.

### 4. Raise the threshold

Use this when more repetition is expected across the whole endpoint:

```csharp
options.ForEndpoint("GET /api/reports/{id}", policy => policy.WithRepeatedQueryThreshold(6));
```

This changes the candidate warning threshold for every query on that endpoint.
It does not change a separate `WithMaxOccurrencesPerFingerprint` budget.
Prefer a specific exception when only one query needs it.

## The reason is not optional

Every allowlist entry needs a reason. State why the repetition is expected and what bounds it.
The reason appears in reports, so avoid sensitive details.

## Ignored findings stay visible

Console, JSON, JUnit, and log output show ignored findings and their reasons.
Use `IgnoredFindingCount` to track the number of exceptions in a result.

## Reporting one

Use the [false-positive form](https://github.com/Benziza/queryguard-dotnet/issues/new?template=false_positive.yml).
Include the query ID, count, redacted SQL, and why the repetition is intentional.

Use synthetic data. Do not include customer data, parameter values, or private schema names.
