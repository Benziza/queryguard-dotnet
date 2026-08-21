using System;
using System.Globalization;
using System.Linq;
using BenchmarkDotNet.Attributes;

namespace QueryGuard.Benchmarks;

/// <summary>
/// What normalizing and fingerprinting one command costs.
/// </summary>
/// <remarks>
/// <para>
/// This runs once per intercepted command, so it is the second-most-executed code in the project after
/// the session lookup. The scenarios separate the two halves: normalization and redaction versus the
/// hash, because if this ever becomes a problem, knowing which half to attack matters more than
/// knowing the total.
/// </para>
/// <para>
/// SQL length is a parameter rather than a fixed value: a wide projection generates SQL an order of
/// magnitude longer than a keyed lookup, and a single number would hide how the cost scales.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class FingerprintBenchmarks
{
    private readonly QueryFingerprintFactory _factory = new();
    private readonly SqlNormalizer _normalizer = new();
    private readonly QueryGuardRedactor _redactor = new();

    private string _sql = string.Empty;

    /// <summary>
    /// Gets or sets how many columns the benchmarked statement projects.
    /// </summary>
    /// <remarks>
    /// Three shapes: a keyed lookup, a typical entity projection, and a wide report query.
    /// </remarks>
    [Params(3, 20, 200)]
    public int ColumnCount { get; set; }

    [GlobalSetup]
    public void Setup()
        => _sql = "SELECT "
            + string.Join(", ", Enumerable.Range(0, ColumnCount).Select(i => $"\"d\".\"Column{i}\""))
            + " FROM \"Departments\" AS \"d\" WHERE \"d\".\"CompanyId\" = @__companyId_0 AND \"d\".\"City\" = 'Paris'";

    /// <summary>
    /// The whole operation the interceptor performs per command.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Normalize + redact + hash")]
    public QueryFingerprint FullFingerprint() => _factory.Create(_sql, QueryCommandKind.Reader);

    /// <summary>
    /// Normalization alone, so its share of the total is visible.
    /// </summary>
    [Benchmark(Description = "Normalize only")]
    public string NormalizeOnly() => _normalizer.Normalize(_sql);

    /// <summary>
    /// Redaction alone, over already-normalized text.
    /// </summary>
    [Benchmark(Description = "Redact only")]
    public string RedactOnly() => _redactor.RedactSql(_sql);
}

/// <summary>
/// What recording a command into a session costs.
/// </summary>
/// <remarks>
/// The comparison that matters is the first one: what QueryGuard costs when it is installed but no
/// scope is open, which is every request outside a measured path. If that is not effectively free, the
/// promise that installing QueryGuard does not change how the application behaves is not true.
/// </remarks>
[MemoryDiagnoser]
public class SessionCaptureBenchmarks
{
    private readonly AsyncLocalQueryGuardSessionAccessor _accessor = new();
    private readonly QueryGuardAnalyzer _analyzer = new();
    private QueryFingerprint _fingerprint = null!;

    /// <summary>
    /// Gets or sets how many commands the benchmarked scope records.
    /// </summary>
    [Params(1, 10, 100)]
    public int CommandCount { get; set; }

    [GlobalSetup]
    public void Setup()
        => _fingerprint = new QueryFingerprint(
            QueryFingerprint.IdPrefix + "1A2B3C4D",
            "SELECT \"d\".\"Id\" FROM \"Departments\" AS \"d\" WHERE \"d\".\"CompanyId\" = ?");

    /// <summary>
    /// The cost of QueryGuard being registered while nothing is being measured.
    /// </summary>
    [Benchmark(Baseline = true, Description = "No active scope")]
    public int NoActiveScope()
    {
        var observed = 0;

        for (var i = 0; i < CommandCount; i++)
        {
            // Exactly what the interceptor does before deciding there is nothing to do.
            if (_accessor.Current is not null)
            {
                observed++;
            }
        }

        return observed;
    }

    /// <summary>
    /// Recording commands into an open scope.
    /// </summary>
    [Benchmark(Description = "Record into an open scope")]
    public int Capture()
    {
        var session = new QueryGuardSession("benchmark", QueryGuardPolicy.Create("benchmark"));

        using (_accessor.Activate(session))
        {
            for (var i = 0; i < CommandCount; i++)
            {
                _accessor.Current!.Record(QueryCommandKind.Reader, _fingerprint, TimeSpan.FromTicks(1));
            }
        }

        return session.Complete().Records.Count;
    }

    /// <summary>
    /// Recording plus the analysis that happens once when the scope closes.
    /// </summary>
    [Benchmark(Description = "Record + analyze")]
    public int CaptureAndAnalyze()
    {
        var session = new QueryGuardSession("benchmark", QueryGuardPolicy.Create("benchmark"));

        using (_accessor.Activate(session))
        {
            for (var i = 0; i < CommandCount; i++)
            {
                _accessor.Current!.Record(QueryCommandKind.Reader, _fingerprint, TimeSpan.FromTicks(1));
            }
        }

        return _analyzer.Analyze(session.Complete()).Groups.Count;
    }
}

/// <summary>
/// What the optional stack trace costs when it is turned on.
/// </summary>
/// <remarks>
/// The point of this benchmark is to justify, or overturn, the default in
/// <c>docs/decisions/0007-stack-trace-policy.md</c>. Capture is bounded to one trace per fingerprint,
/// so the parameter that matters is how many <em>distinct</em> queries a scope sees, not how many
/// commands it runs.
/// </remarks>
[MemoryDiagnoser]
public class StackTraceBenchmarks
{
    private const int CommandsPerFingerprint = 10;

    private readonly AsyncLocalQueryGuardSessionAccessor _accessor = new();
    private readonly QueryGuardRedactor _withoutCapture = new();
    private readonly QueryGuardRedactor _withCapture =
        new(new QueryGuardCaptureOptions { CaptureFirstStackTrace = true });

    private QueryFingerprint[] _fingerprints = [];

    /// <summary>
    /// Gets or sets how many distinct fingerprints the scope sees.
    /// </summary>
    [Params(1, 10)]
    public int FingerprintCount { get; set; }

    [GlobalSetup]
    public void Setup()
        => _fingerprints = [.. Enumerable.Range(0, FingerprintCount).Select(index => new QueryFingerprint(
            QueryFingerprint.IdPrefix + index.ToString("X8", CultureInfo.InvariantCulture),
            string.Create(CultureInfo.InvariantCulture, $"SELECT * FROM \"T{index}\" WHERE \"Id\" = ?")))];

    /// <summary>
    /// The default: capture disabled, so the provider callback is never invoked.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Stack trace off (default)")]
    public int Off() => Run(_withoutCapture);

    /// <summary>
    /// Capture enabled, bounded to the first occurrence of each fingerprint.
    /// </summary>
    [Benchmark(Description = "Stack trace on, first occurrence only")]
    public int FirstOccurrenceOnly() => Run(_withCapture);

    private int Run(QueryGuardRedactor redactor)
    {
        var session = new QueryGuardSession("benchmark", QueryGuardPolicy.Create("benchmark"), redactor);

        using (_accessor.Activate(session))
        {
            for (var command = 0; command < CommandsPerFingerprint; command++)
            {
                foreach (var fingerprint in _fingerprints)
                {
                    session.Record(
                        QueryCommandKind.Reader,
                        fingerprint,
                        TimeSpan.FromTicks(1),
                        stackTraceProvider: static () => Environment.StackTrace);
                }
            }
        }

        return session.Complete().Records.Count;
    }
}
