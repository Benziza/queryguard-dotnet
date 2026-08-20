using System;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace QueryGuard.Benchmarks;

/// <summary>
/// Entry point for the QueryGuard benchmark suite.
/// </summary>
/// <remarks>
/// <para>
/// Run everything: <c>dotnet run -c Release --project benchmarks/QueryGuard.Benchmarks</c>
/// </para>
/// <para>
/// Run one set: <c>… -- --filter *FingerprintBenchmarks*</c>
/// </para>
/// <para>
/// A smoke run that proves the harness works without waiting for statistical convergence:
/// <c>… -- --filter * --job Dry</c>. That is what CI uses — a benchmark job that gates a pull request
/// would either be too slow to be useful or too short to be accurate.
/// </para>
/// <para>
/// Every published number must carry the hardware, OS, .NET version, EF Core version, provider,
/// scenario configuration, sample size, and source commit that produced it. BenchmarkDotNet prints all
/// of that in its header — publish the header with the table, or the table means nothing. See
/// <c>docs/testing-strategy.md</c>.
/// </para>
/// </remarks>
internal static class Program
{
    private static void Main(string[] args)
        => BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args, DefaultConfig.Instance.WithOptions(ConfigOptions.DisableOptimizationsValidator));
}
