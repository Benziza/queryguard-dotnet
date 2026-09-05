# Benchmarks

These benchmarks measure QueryGuard code with generated SQL. They do not include a database,
network, HTTP request, or EF Core command execution.

Use them to compare capture options. Measure your application to estimate request overhead.
Short runs have wide error margins; treat these results as estimates.

## Environment

The following tables, except the full SQL update, use this environment:

| Setting | Value |
| --- | --- |
| Source | [`46ec17e`](https://github.com/Benziza/queryguard-dotnet/commit/46ec17ebb9704694e71eb67f73ead60e5556ecca) |
| Date | 2026-08-20 |
| BenchmarkDotNet | 0.15.8 |
| Job | ShortRun: 3 warmup iterations, 3 measured iterations, 1 launch |
| OS | Windows 11 (10.0.26200.9168/25H2) |
| CPU | Intel Core Ultra 5 225F, 3.30 GHz, 10 physical / 10 logical cores |
| Runtime | SDK 10.0.302, .NET 10.0.10, X64 RyuJIT `x86-64-v3` |
| EF Core / provider | Not exercised / none |

[Raw reports and CSV files](https://github.com/Benziza/queryguard-dotnet/tree/main/docs/benchmarks/2026-08-20-46ec17e)

Reproduce with:

```bash
dotnet run -c Release --project benchmarks/QueryGuard.Benchmarks -- --filter "*" --job Short
```

Use the default benchmark job on a quiet machine for more reliable measurements.
CI uses `--job Dry` only to check that the benchmarks run.

## No active scope

This measures the session lookup when no scope is open.

| Commands | Mean | Allocated |
| --- | --- | --- |
| 1 | 1.11 ns | 0 B |
| 10 | 7.35 ns | 0 B |
| 100 | 91.81 ns | 0 B |

## Capturing and analysing a scope

| Commands | Record | Record + analyse | Allocated (record + analyse) |
| --- | --- | --- | --- |
| 1 | 271 ns | 375 ns | 2,080 B |
| 10 | 770 ns | 1,359 ns | 4,488 B |
| 100 | 5,586 ns | 7,064 ns | 15,728 B |

Analysis runs once when the scope ends. These capture measurements do not include
the separate fingerprint work below.

## Fingerprinting

Results at the August source revision:

| Columns in the statement | Full | Normalize only | Redact only |
| --- | --- | --- | --- |
| 3 | 584 ns | 162 ns | 165 ns |
| 20 | 1,138 ns | 486 ns | 482 ns |
| 200 | 7,199 ns | 3,189 ns | 3,459 ns |

## Full SQL fingerprints (2026-09-05)

This update measures hashing the full redacted SQL before display truncation at
[`aa8f6f8`](https://github.com/Benziza/queryguard-dotnet/commit/aa8f6f829de774d46c4badb77c10633c09ec9feb).
The 700-column case exceeds the default 4096-character display limit.

Environment: Windows 11 (10.0.26200.9168), Intel Core Ultra 5 225F (10 cores),
SDK 10.0.400, .NET 10.0.11, BenchmarkDotNet 0.15.8.
ShortRun used one launch, three warmup iterations, and three measured iterations.
No database or EF Core command execution was involved.

```bash
dotnet run -c Release --project benchmarks/QueryGuard.Benchmarks -- --filter "*FingerprintBenchmarks.FullFingerprint*" --job Short
```

| Columns | Mean | Error (99.9% CI half-width) | Allocated per operation |
| --- | --- | --- | --- |
| 3 | 0.65 us | 0.03 us | 1.98 KB |
| 20 | 1.34 us | 0.33 us | 4.84 KB |
| 200 | 9.05 us | 3.11 us | 36.85 KB |
| 700 | 29.93 us | 8.91 us | 136.26 KB |

[BenchmarkDotNet report](./benchmarks/2026-09-05-aa8f6f8/QueryGuard.Benchmarks.FingerprintBenchmarks-report-github.md)
and [CSV output](./benchmarks/2026-09-05-aa8f6f8/QueryGuard.Benchmarks.FingerprintBenchmarks-report.csv).

## Stack-trace capture: why it is off by default

| Distinct fingerprints | Off | On, first occurrence only | Slower by | More allocation by |
| --- | --- | --- | --- | --- |
| 1 | 722 ns | 15,720 ns | 22× | 18× |
| 10 | 5,337 ns | 153,702 ns | 29× | 26× |

Each scenario records ten commands per fingerprint.
Enabling one filtered trace per fingerprint made these scenarios 20–30 times slower.

Request stack traces are off by default. Test scopes capture origins by default and allow
`captureOrigin: false`. See [capture settings](./configuration/README.md#where-a-repeated-query-came-from).