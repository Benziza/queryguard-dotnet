using System;

namespace QueryGuard;

/// <summary>
/// What one scope cost when the baseline was recorded.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately three numbers and a name. A baseline that recorded SQL text would be a second copy of
/// the report, would need redaction rules of its own, and would produce a diff nobody reads on every
/// unrelated schema change. Counts are the thing a regression moves.
/// </para>
/// <para>
/// No timings either. Durations vary between a laptop and a shared runner, so a baseline containing
/// them would report a regression whenever CI was busy — which is the failure mode that teaches people
/// to ignore a tool.
/// </para>
/// </remarks>
public sealed class QueryGuardBaselineEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardBaselineEntry"/> class.
    /// </summary>
    /// <param name="scope">The scope this entry describes — a route pattern or a test name.</param>
    /// <param name="readCommands">Counted read commands the scope executed.</param>
    /// <param name="distinctQueries">How many distinct fingerprints it executed.</param>
    /// <param name="topFingerprintOccurrences">
    /// How many times the most repeated fingerprint ran. This is the number that moves when a
    /// projection turns back into a loop, and it moves even when the total stays flat.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count is negative.</exception>
    public QueryGuardBaselineEntry(
        string scope,
        int readCommands,
        int distinctQueries,
        int topFingerprintOccurrences)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("A baseline entry needs a scope name.", nameof(scope));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(readCommands);
        ArgumentOutOfRangeException.ThrowIfNegative(distinctQueries);
        ArgumentOutOfRangeException.ThrowIfNegative(topFingerprintOccurrences);

        Scope = scope;
        ReadCommands = readCommands;
        DistinctQueries = distinctQueries;
        TopFingerprintOccurrences = topFingerprintOccurrences;
    }

    /// <summary>
    /// Gets the scope this entry describes.
    /// </summary>
    public string Scope { get; }

    /// <summary>
    /// Gets the counted read commands recorded for this scope.
    /// </summary>
    public int ReadCommands { get; }

    /// <summary>
    /// Gets how many distinct fingerprints the scope executed.
    /// </summary>
    public int DistinctQueries { get; }

    /// <summary>
    /// Gets how many times the most repeated fingerprint ran.
    /// </summary>
    public int TopFingerprintOccurrences { get; }

    /// <summary>
    /// Records what a completed result cost, so it can be compared later.
    /// </summary>
    /// <param name="result">The result to record.</param>
    /// <returns>An entry describing it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static QueryGuardBaselineEntry FromResult(QueryGuardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new QueryGuardBaselineEntry(
            result.SessionName,
            result.ReadCommandCount,
            result.Groups.Count,
            result.TopRepeatedGroup?.Occurrences ?? 0);
    }

    /// <inheritdoc />
    public override string ToString()
        => $"{Scope}: {ReadCommands} reads, {DistinctQueries} distinct, top x{TopFingerprintOccurrences}";
}
