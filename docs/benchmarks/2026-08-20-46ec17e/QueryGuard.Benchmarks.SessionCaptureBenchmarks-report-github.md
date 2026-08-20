```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
Intel Core Ultra 5 225F 3.30GHz, 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                      | CommandCount | Mean         | Error       | StdDev     | Ratio  | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------------------- |------------- |-------------:|------------:|-----------:|-------:|--------:|-------:|-------:|----------:|------------:|
| **&#39;No active scope&#39;**           | **1**            |     **1.112 ns** |   **0.3552 ns** |  **0.0195 ns** |   **1.00** |    **0.02** |      **-** |      **-** |         **-** |          **NA** |
| &#39;Record into an open scope&#39; | 1            |   271.432 ns |  23.0803 ns |  1.2651 ns | 244.09 |    3.84 | 0.1178 |      - |    1232 B |          NA |
| &#39;Record + analyze&#39;          | 1            |   375.227 ns |  53.6967 ns |  2.9433 ns | 337.43 |    5.62 | 0.1988 | 0.0005 |    2080 B |          NA |
|                             |              |              |             |            |        |         |        |        |           |             |
| **&#39;No active scope&#39;**           | **10**           |     **7.351 ns** |   **0.4556 ns** |  **0.0250 ns** |   **1.00** |    **0.00** |      **-** |      **-** |         **-** |          **NA** |
| &#39;Record into an open scope&#39; | 10           |   769.669 ns | 178.2487 ns |  9.7704 ns | 104.70 |    1.19 | 0.2298 | 0.0010 |    2408 B |          NA |
| &#39;Record + analyze&#39;          | 10           | 1,359.157 ns | 752.1130 ns | 41.2258 ns | 184.89 |    4.89 | 0.4292 | 0.0019 |    4488 B |          NA |
|                             |              |              |             |            |        |         |        |        |           |             |
| **&#39;No active scope&#39;**           | **100**          |    **91.811 ns** | **234.7280 ns** | **12.8662 ns** |   **1.01** |    **0.18** |      **-** |      **-** |         **-** |          **NA** |
| &#39;Record into an open scope&#39; | 100          | 5,585.694 ns | 924.3116 ns | 50.6646 ns |  61.72 |    8.16 | 1.2970 |      - |   13632 B |          NA |
| &#39;Record + analyze&#39;          | 100          | 7,064.487 ns | 760.6607 ns | 41.6944 ns |  78.06 |   10.31 | 1.5030 | 0.0610 |   15728 B |          NA |
