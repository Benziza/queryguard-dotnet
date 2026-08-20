using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using QueryGuard;
using QueryGuard.Reporting;
using QueryGuard.Testing;
using Xunit;
using Xunit.Abstractions;

namespace QueryGuard.SampleTests;

/// <summary>
/// The baseline comparison, wired the way a real project would wire it.
/// </summary>
/// <remarks>
/// <para>
/// A budget asks for a number nobody has. A baseline asks for nothing: it records what the endpoints
/// cost today and reports what changed. This measures all three sample endpoints, compares them against
/// the committed <c>queryguard-baseline.json</c>, and writes the Markdown table to
/// <c>artifacts/queryguard/summary.md</c> for the QueryGuard action to publish.
/// </para>
/// <para>
/// It is also the regression test for the sample itself. The numbers in the README and in
/// <c>samples/README.md</c> are the numbers in that baseline file, so if an endpoint's query count ever
/// moves, this fails and says which one — rather than the documentation quietly becoming wrong.
/// </para>
/// </remarks>
public sealed class BaselineComparisonDemoTests : IClassFixture<SampleApiFactory>
{
    private const string BaselineFileName = "queryguard-baseline.json";

    private readonly SampleApiFactory _factory;
    private readonly ITestOutputHelper _output;

    public BaselineComparisonDemoTests(SampleApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task The_sample_endpoints_still_cost_what_the_baseline_says()
    {
        var results = await MeasureEveryEndpointAsync();

        var baseline = QueryGuardBaseline.FromJson(await File.ReadAllTextAsync(FindBaseline()));
        var comparison = QueryGuardBaselineComparison.Compare(baseline, results);

        var markdown = new QueryGuardBaselineMarkdownReporter().Render(comparison);
        _output.WriteLine(markdown);

        await WriteSummaryAsync(markdown);

        // Every sample endpoint is deterministic, so anything here is a real change in the sample and
        // the committed numbers in both READMEs are now wrong.
        Assert.Empty(comparison.Regressions);
        Assert.Empty(comparison.NewScopes);
    }

    [Fact]
    public async Task A_regression_against_an_older_baseline_is_reported_as_one()
    {
        // The demonstration. This baseline is what the catalogue looked like before anyone wrote the
        // per-company loop: three queries for the company list endpoint. Comparing the current code
        // against it produces the sentence the whole feature exists for.
        var before = QueryGuardBaseline.Empty
            .Record(new QueryGuardBaselineEntry("GET /api/companies", readCommands: 3, distinctQueries: 2, topFingerprintOccurrences: 2));

        var results = await MeasureEveryEndpointAsync();
        var comparison = QueryGuardBaselineComparison.Compare(before, results);

        var regression = Assert.Single(comparison.Regressions);

        Assert.Equal("GET /api/companies", regression.Scope);
        Assert.Equal(3, regression.Baseline!.ReadCommands);
        Assert.Equal(51, regression.Current.ReadCommands);
        Assert.Equal(48, regression.ReadCommandDelta);

        _output.WriteLine(new QueryGuardBaselineMarkdownReporter().Render(comparison));
    }

    /// <summary>
    /// Rewrites the committed baseline from a live run.
    /// </summary>
    /// <remarks>
    /// Skipped, because accepting a change in what the sample costs should be a deliberate act rather
    /// than something a test run does on its own. Remove the <c>Skip</c>, run this one test, and commit
    /// the file — the diff is then the record of the decision. This is the same workflow a real project
    /// follows, which is why it lives here rather than in a script.
    /// </remarks>
    [Fact(Skip = "Maintenance helper. Remove the Skip, run it, and commit the file it writes.")]
    public async Task Regenerate_the_committed_baseline()
    {
        var results = await MeasureEveryEndpointAsync();

        var baseline = QueryGuardBaseline.Empty;
        foreach (var result in results)
        {
            baseline = baseline.Record(result);
        }

        var path = Path.Join(
            new DirectoryInfo(AppContext.BaseDirectory).Parent!.Parent!.Parent!.FullName,
            BaselineFileName);

        await File.WriteAllTextAsync(path, baseline.ToJson());
        _output.WriteLine($"wrote {path}");
        _output.WriteLine(baseline.ToJson());
    }

    /// <summary>
    /// Measures every sample endpoint in its own scope.
    /// </summary>
    private async Task<List<QueryGuardResult>> MeasureEveryEndpointAsync()
    {
        var routes = new[]
        {
            "/api/companies",
            "/api/companies/projected",
            "/api/reports/summary",
        };

        var results = new List<QueryGuardResult>(routes.Length);

        using var client = _factory.CreateClient();

        foreach (var route in routes)
        {
            results.Add(await MeasureAsync(client, route));
        }

        return results;
    }

    private async Task<QueryGuardResult> MeasureAsync(HttpClient client, string route)
    {
        await using var scope = QueryGuardScope.Start(
            $"GET {route}",
            QueryGuardPolicy.Create("baseline"),
            accessor: _factory.SessionAccessor);

        using var response = await client.GetAsync(new Uri(route, UriKind.Relative));
        response.EnsureSuccessStatusCode();

        return await scope.CompleteAsync();
    }

    /// <summary>
    /// Writes the table where the QueryGuard action will find it.
    /// </summary>
    /// <remarks>
    /// A file rather than  directly. The test's job is to measure and render;
    /// deciding where a report goes — job summary, pull request comment, both — belongs to whatever
    /// publishes it, and writing a file works identically on a laptop and in CI.
    /// </remarks>
    private static async Task WriteSummaryAsync(string markdown)
    {
        // Anchored at the repository root, not at the working directory.
        //
        // A test host runs with its output folder as the working directory, so a relative path lands
        // in bin/Release/net10.0/artifacts and CI looks for it in the workspace root and finds
        // nothing. The report is then silently missing rather than wrong, which is the worst kind of
        // missing. Any real project hits this, which is why the action's README says to use a
        // root-anchored path.
        var root = RepositoryRoot();
        var path = Path.Join(root, "artifacts", "queryguard", "summary.md");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, markdown);
    }

    /// <summary>
    /// The repository root, found by walking up for the solution file.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "QueryGuard.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        // Falling back to the working directory keeps the test passing outside a checkout; the report
        // just lands somewhere less convenient.
        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Walks up from the test assembly to find the committed baseline.
    /// </summary>
    /// <remarks>
    /// The file lives beside the sample sources rather than being copied to the output directory, so
    /// that regenerating it updates the committed file and not a build artifact that vanishes on the
    /// next clean.
    /// </remarks>
    private static string FindBaseline()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Join(directory.FullName, BaselineFileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find {BaselineFileName} above {AppContext.BaseDirectory}. "
            + "Regenerate it with the recording snippet in docs/baselines/README.md.");
    }
}
