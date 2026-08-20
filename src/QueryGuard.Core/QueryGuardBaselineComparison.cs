using System;
using System.Collections.Generic;
using System.Linq;

namespace QueryGuard;

/// <summary>
/// How one scope compares to what it cost when the baseline was recorded.
/// </summary>
/// <remarks>
/// The useful sentence a tool can say about a pull request is not "this endpoint runs 51 queries" — it
/// is "this pull request changed this endpoint from 3 to 51". The first needs a reader who knows what
/// good looks like. The second does not.
/// </remarks>
public sealed class QueryGuardScopeComparison
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardScopeComparison"/> class.
    /// </summary>
    /// <param name="scope">The scope compared.</param>
    /// <param name="baseline">What it cost before, or <see langword="null"/> when it is new.</param>
    /// <param name="current">What it costs now.</param>
    /// <exception cref="ArgumentNullException"><paramref name="current"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is empty or whitespace.</exception>
    public QueryGuardScopeComparison(
        string scope,
        QueryGuardBaselineEntry? baseline,
        QueryGuardBaselineEntry current)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("A comparison needs a scope name.", nameof(scope));
        }

        ArgumentNullException.ThrowIfNull(current);

        Scope = scope;
        Baseline = baseline;
        Current = current;
    }

    /// <summary>
    /// Gets the scope compared.
    /// </summary>
    public string Scope { get; }

    /// <summary>
    /// Gets what the scope cost when the baseline was recorded, or <see langword="null"/> when it is new.
    /// </summary>
    public QueryGuardBaselineEntry? Baseline { get; }

    /// <summary>
    /// Gets what the scope costs now.
    /// </summary>
    public QueryGuardBaselineEntry Current { get; }

    /// <summary>
    /// Gets whether this scope has no baseline to compare against.
    /// </summary>
    /// <remarks>
    /// A new scope is not a regression. Treating it as one would fail the pull request that adds any
    /// endpoint, which is the fastest way to get a check turned off.
    /// </remarks>
    public bool IsNew => Baseline is null;

    /// <summary>
    /// Gets the change in counted read commands. Positive means more queries than before.
    /// </summary>
    public int ReadCommandDelta => Current.ReadCommands - (Baseline?.ReadCommands ?? Current.ReadCommands);

    /// <summary>
    /// Gets the change in how many times the most repeated fingerprint ran.
    /// </summary>
    /// <remarks>
    /// Worth reporting separately from the total, because it moves when the total does not. Replacing
    /// twenty distinct lookups with one query repeated twenty times leaves the read count identical and
    /// is exactly the regression QueryGuard exists to catch.
    /// </remarks>
    public int TopFingerprintDelta
        => Current.TopFingerprintOccurrences - (Baseline?.TopFingerprintOccurrences ?? Current.TopFingerprintOccurrences);

    /// <summary>
    /// Gets whether this scope got worse.
    /// </summary>
    public bool IsRegression => !IsNew && (ReadCommandDelta > 0 || TopFingerprintDelta > 0);

    /// <summary>
    /// Gets whether this scope got better.
    /// </summary>
    /// <remarks>
    /// Reported as well as regressions, so a pull request that fixes an N+1 gets to show it. A tool
    /// that only ever delivers bad news is one people stop reading.
    /// </remarks>
    public bool IsImprovement => !IsNew && ReadCommandDelta < 0;

    /// <inheritdoc />
    public override string ToString()
        => IsNew
            ? $"{Scope}: new, {Current.ReadCommands} queries"
            : $"{Scope}: {Baseline!.ReadCommands} -> {Current.ReadCommands} queries";
}

/// <summary>
/// The result of comparing a set of measured scopes against a baseline.
/// </summary>
public sealed class QueryGuardBaselineComparison
{
    private QueryGuardBaselineComparison(IReadOnlyList<QueryGuardScopeComparison> scopes) => Scopes = scopes;

    /// <summary>
    /// Gets every compared scope, regressions first and then by name.
    /// </summary>
    /// <remarks>
    /// Ordered so the interesting rows are at the top of a pull request comment, and deterministically
    /// so two runs over the same data produce the same output.
    /// </remarks>
    public IReadOnlyList<QueryGuardScopeComparison> Scopes { get; }

    /// <summary>
    /// Gets the scopes that got worse.
    /// </summary>
    public IReadOnlyList<QueryGuardScopeComparison> Regressions
        => [.. Scopes.Where(scope => scope.IsRegression)];

    /// <summary>
    /// Gets the scopes that got better.
    /// </summary>
    public IReadOnlyList<QueryGuardScopeComparison> Improvements
        => [.. Scopes.Where(scope => scope.IsImprovement)];

    /// <summary>
    /// Gets the scopes measured for the first time.
    /// </summary>
    public IReadOnlyList<QueryGuardScopeComparison> NewScopes
        => [.. Scopes.Where(scope => scope.IsNew)];

    /// <summary>
    /// Gets whether anything got worse.
    /// </summary>
    public bool HasRegressions => Scopes.Any(scope => scope.IsRegression);

    /// <summary>
    /// Compares measured results against a baseline.
    /// </summary>
    /// <param name="baseline">The recorded baseline. <see cref="QueryGuardBaseline.Empty"/> is valid.</param>
    /// <param name="results">The results measured in this run.</param>
    /// <returns>The comparison.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Scopes present in the baseline but absent from this run are ignored rather than reported as
    /// removed. A test run filtered to one project would otherwise claim every other endpoint had been
    /// deleted, and being wrong that loudly is worse than saying nothing.
    /// </remarks>
    public static QueryGuardBaselineComparison Compare(
        QueryGuardBaseline baseline,
        IEnumerable<QueryGuardResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return CompareEntries(
            baseline,
            results.Where(result => result is not null).Select(QueryGuardBaselineEntry.FromResult));
    }

    /// <summary>
    /// Compares recorded measurements against a baseline.
    /// </summary>
    /// <param name="baseline">The recorded baseline. <see cref="QueryGuardBaseline.Empty"/> is valid.</param>
    /// <param name="measured">What each scope cost in this run.</param>
    /// <returns>The comparison.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// For measurements that did not come from a live run — read back from JSON reports, for instance,
    /// which is how the command-line tool works. An entry is what a comparison actually needs; a result
    /// is just a convenient thing to derive one from.
    /// </para>
    /// <para>
    /// A separate name rather than an overload, because two overloads differing only in element type
    /// make <c>Compare(baseline, [])</c> ambiguous — a collection expression has nothing to infer from.
    /// The compiler caught that on the existing tests, which is a better place to find it than a user's
    /// build.
    /// </para>
    /// <para>
    /// Scopes present in the baseline but absent from <paramref name="measured"/> are ignored rather than
    /// reported as removed. A test run filtered to one project would otherwise claim every other
    /// endpoint had been deleted, and being wrong that loudly is worse than saying nothing.
    /// </para>
    /// </remarks>
    public static QueryGuardBaselineComparison CompareEntries(
        QueryGuardBaseline baseline,
        IEnumerable<QueryGuardBaselineEntry> measured)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(measured);

        var scopes = new List<QueryGuardScopeComparison>();

        foreach (var current in measured)
        {
            if (current is null)
            {
                continue;
            }

            scopes.Add(new QueryGuardScopeComparison(
                current.Scope,
                baseline.Find(current.Scope),
                current));
        }

        return new QueryGuardBaselineComparison(
            [.. scopes
                .OrderByDescending(scope => scope.IsRegression)
                .ThenByDescending(scope => scope.ReadCommandDelta)
                .ThenBy(scope => scope.Scope, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Returns a baseline updated with everything measured in this run.
    /// </summary>
    /// <param name="baseline">The baseline to update.</param>
    /// <returns>A new baseline; the original is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="baseline"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Accepting a regression is a deliberate act: regenerate the baseline, commit it, and the diff
    /// shows a reviewer that a scope went from 3 to 51 and somebody decided that was fine.
    /// </remarks>
    public QueryGuardBaseline Accept(QueryGuardBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        var updated = baseline;

        foreach (var scope in Scopes)
        {
            updated = updated.Record(scope.Current);
        }

        return updated;
    }
}
