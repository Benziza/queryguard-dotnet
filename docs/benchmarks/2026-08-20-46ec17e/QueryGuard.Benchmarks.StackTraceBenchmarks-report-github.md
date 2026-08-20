```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
Intel Core Ultra 5 225F 3.30GHz, 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.302
  [Host]   : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                  | FingerprintCount | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|---------------------------------------- |----------------- |-------------:|------------:|------------:|------:|--------:|--------:|-------:|----------:|------------:|
| **&#39;Stack trace off (default)&#39;**             | **1**                |     **722.0 ns** |    **104.7 ns** |     **5.74 ns** |  **1.00** |    **0.01** |  **0.1984** |      **-** |   **2.03 KB** |        **1.00** |
| &#39;Stack trace on, first occurrence only&#39; | 1                |  15,719.9 ns | 12,646.8 ns |   693.21 ns | 21.77 |    0.84 |  3.5400 | 0.1221 |  36.49 KB |       17.96 |
|                                         |                  |              |             |             |       |         |         |        |           |             |
| **&#39;Stack trace off (default)&#39;**             | **10**               |   **5,337.2 ns** |    **895.0 ns** |    **49.06 ns** |  **1.00** |    **0.01** |  **1.3275** | **0.0610** |  **13.56 KB** |        **1.00** |
| &#39;Stack trace on, first occurrence only&#39; | 10               | 153,701.8 ns | 92,777.2 ns | 5,085.43 ns | 28.80 |    0.86 | 34.6680 | 4.8828 | 357.85 KB |       26.39 |
