using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QueryGuard.AspNetCore;
using QueryGuard.Reporting;
using QueryGuard.SampleApi;
using QueryGuard.Testing;
using Xunit;
using Xunit.Abstractions;

namespace QueryGuard.SampleTests;

/// <summary>
/// The demonstration this project exists for: the same response, once with an N+1 and once without.
/// </summary>
/// <remarks>
/// <para>
/// The README quotes these tests. They are written to be read as much as to be run, and the numbers in
/// them are exact because the sample data is seeded deterministically.
/// </para>
/// <para>
/// <see cref="The_problem_endpoint_returns_200_OK_and_still_breaks_its_query_budget"/> is the
/// interesting one — it asserts that a QueryGuard budget failure <em>does</em> happen. That is a
/// passing test about a failing budget, not a broken build.
/// </para>
/// </remarks>
public sealed class CompanyEndpointDemoTests : IClassFixture<SampleApiFactory>
{
    private const int SeededCompanies = 50;

    private readonly SampleApiFactory _factory;
    private readonly ITestOutputHelper _output;

    public CompanyEndpointDemoTests(SampleApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task The_problem_endpoint_returns_200_OK_and_still_breaks_its_query_budget()
    {
        using var client = _factory.CreateClient();

        await using var scope = QueryGuardScope.Start(
            "GET /api/companies",
            QueryGuardPolicy.Create("companies").WithMaxOccurrencesPerFingerprint(5),
            accessor: _factory.SessionAccessor);

        var response = await client.GetAsync(new Uri("/api/companies", UriKind.Relative));
        var payload = await response.Content.ReadFromJsonAsync<List<CompanySummary>>();

        // Nothing about the response is wrong. That is the whole point.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SeededCompanies, payload!.Count);
        Assert.All(payload, company => Assert.Equal(3, company.DepartmentCount));

        var result = await scope.CompleteAsync();
        _output.WriteLine(new QueryGuardConsoleReporter().Render(result));

        // One query for the list, then one per company.
        Assert.Equal(SeededCompanies + 1, result.ReadCommandCount);

        var repeated = Assert.Single(result.Groups, group => group.Occurrences > 1);
        Assert.Equal(SeededCompanies, repeated.Occurrences);
        Assert.Contains("Departments", repeated.Fingerprint.NormalizedSql, StringComparison.Ordinal);

        // The assertion a real test would make. Here it is expected to fail, so it is inverted — and
        // the message is printed, because that message is what a developer would actually see.
        var failure = Assert.Throws<QueryGuardBudgetExceededException>(() => QueryGuardAssert.Passes(result));
        _output.WriteLine(failure.Message);

        Assert.Contains("executed 50 times", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fixed_endpoint_returns_the_same_data_from_one_query()
    {
        using var client = _factory.CreateClient();

        await using var scope = QueryGuardScope.Start(
            "GET /api/companies/projected",
            QueryGuardPolicy.Create("companies")
                .WithMaxQueries(3)
                .WithMaxOccurrencesPerFingerprint(1),
            accessor: _factory.SessionAccessor);

        var response = await client.GetAsync(new Uri("/api/companies/projected", UriKind.Relative));
        var payload = await response.Content.ReadFromJsonAsync<List<CompanySummary>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SeededCompanies, payload!.Count);
        Assert.All(payload, company => Assert.Equal(3, company.DepartmentCount));

        var result = await scope.CompleteAsync();
        _output.WriteLine(new QueryGuardConsoleReporter().Render(result));

        Assert.Equal(1, result.ReadCommandCount);

        // The assertion as it would be written for real, passing.
        QueryGuardAssert.Passes(result);
        QueryGuardAssert.ExecutedQueryCount(1, result);
    }

    [Fact]
    public async Task Both_endpoints_return_identical_data()
    {
        // The fix has to be a fix, not a different answer that happens to be cheaper.
        using var client = _factory.CreateClient();

        var problem = await client.GetFromJsonAsync<List<CompanySummary>>(
            new Uri("/api/companies", UriKind.Relative));
        var fixedVersion = await client.GetFromJsonAsync<List<CompanySummary>>(
            new Uri("/api/companies/projected", UriKind.Relative));

        Assert.Equal(
            problem!.OrderBy(company => company.Id).ToList(),
            fixedVersion!.OrderBy(company => company.Id).ToList());
    }

    [Fact]
    public async Task An_intentional_repetition_is_reported_as_ignored_with_its_reason()
    {
        // Three lookups, bounded by the shape of the report rather than by the number of rows. The tag
        // documents that next to the query, and the finding stays visible with its reason.
        using var client = _factory.CreateClient();

        await using var scope = QueryGuardScope.Start(
            "GET /api/reports/summary",
            QueryGuardPolicy.Create("reports").WithMaxOccurrencesPerFingerprint(1),
            accessor: _factory.SessionAccessor);

        var response = await client.GetAsync(new Uri("/api/reports/summary", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await scope.CompleteAsync();
        _output.WriteLine(new QueryGuardConsoleReporter().Render(result));

        Assert.All(result.Findings, finding => Assert.True(finding.IsIgnored));
        Assert.Contains(
            result.Findings,
            finding => finding.IgnoreReason!.Contains("bounded-by-layout", StringComparison.Ordinal));

        // Ignored findings do not fail, and they are not hidden either.
        QueryGuardAssert.Passes(result);
        Assert.True(result.IgnoredFindingCount > 0);
    }

    [Fact]
    public async Task The_json_and_junit_reports_render_the_failing_run()
    {
        // What a CI job would upload as an artifact.
        using var client = _factory.CreateClient();

        await using var scope = QueryGuardScope.Start(
            "GET /api/companies",
            QueryGuardPolicy.Create("companies").WithMaxOccurrencesPerFingerprint(5),
            accessor: _factory.SessionAccessor);

        _ = await client.GetAsync(new Uri("/api/companies", UriKind.Relative));

        var result = await scope.CompleteAsync();

        var json = new QueryGuardJsonReporter().Render(result);
        var junit = new QueryGuardJUnitReporter().Render(result);

        _output.WriteLine(junit);

        Assert.Contains("\"schemaVersion\": \"1.0\"", json, StringComparison.Ordinal);
        Assert.Contains("<failure", junit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sarif_report_points_code_scanning_at_the_line_that_ran_the_query()
    {
        // This repository uploads the file this test writes, so the SARIF reporter is exercised against
        // real findings on every pull request rather than only against fixtures. A document can be valid
        // SARIF, upload without complaint, and still annotate nothing — the assertion that matters is
        // that a location survived all the way to the emitted URI.
        using var client = _factory.CreateClient();

        await using var scope = QueryGuardScope.Start(
            "GET /api/companies",
            QueryGuardPolicy.Create("companies").WithMaxOccurrencesPerFingerprint(5),
            accessor: _factory.SessionAccessor);

        _ = await client.GetAsync(new Uri("/api/companies", UriKind.Relative));

        var result = await scope.CompleteAsync();
        var root = RepositoryRoot();
        // The fallback is where a finding with no captured origin is attached. GitHub rejects a whole
        // SARIF file if any result has no location, so the choice is a real one rather than cosmetic:
        // the test that measured the endpoint is an honest place to point, and issue #109 is why the
        // per-fingerprint budget finding needs it at all.
        var sarif = new QueryGuardSarifReporter(
            root,
            fallbackPath: "samples/QueryGuard.SampleTests/CompanyEndpointDemoTests.cs").Render(result);

        Assert.Contains("\"version\": \"2.1.0\"", sarif, StringComparison.Ordinal);
        Assert.Contains(RuleNames.MaxOccurrencesPerFingerprint, sarif, StringComparison.Ordinal);

        // Relative, so GitHub can match it against the diff, and pointing into the sample rather than
        // into the test project.
        Assert.Contains("samples/QueryGuard.SampleApi/Program.cs", sarif, StringComparison.Ordinal);
        Assert.DoesNotContain(root.Replace('\\', '/'), sarif, StringComparison.Ordinal);

        var path = Path.Join(root, "artifacts", "queryguard", "queryguard.sarif");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, sarif);
    }

    /// <summary>
    /// The repository root, found by walking up for the solution file.
    /// </summary>
    /// <remarks>
    /// A test host runs with its output folder as the working directory, so a relative path would land
    /// in <c>bin/Release/net10.0/artifacts</c> and CI would look in the workspace root and find nothing.
    /// The report is then silently missing rather than wrong, which is the worst kind of missing.
    /// </remarks>
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

        throw new InvalidOperationException(
            "Could not find QueryGuard.slnx above " + AppContext.BaseDirectory + ".");
    }
}

/// <summary>
/// Hosts the sample API over an in-memory SQLite database.
/// </summary>
/// <remarks>
/// <para>
/// The sample's own configuration writes to a file, which is right for running it by hand and wrong for
/// a test. This factory swaps in a named shared-cache in-memory database so the suite leaves nothing
/// behind and each context can open its own connection.
/// </para>
/// <para>
/// It also exposes the application's session accessor. A scope opened in the test and the interceptor
/// running inside the request have to agree on which accessor they read, or the scope captures nothing.
/// </para>
/// </remarks>
public sealed class SampleApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = string.Create(
        CultureInfo.InvariantCulture,
        $"Data Source=queryguard-sample-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    private SqliteConnection? _keepAlive;

    /// <summary>
    /// Gets the accessor the hosted application's interceptor reads.
    /// </summary>
    public IQueryGuardSessionAccessor SessionAccessor
        => Services.GetRequiredService<IQueryGuardSessionAccessor>();

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development, because the sample only enables QueryGuard outside production.
        builder.UseEnvironment("Development");

        // Redirect the sample's own connection string rather than re-registering its DbContext.
        // Re-registering would apply the sample's configuration action *and* the replacement, attaching
        // the interceptor twice and doubling every recorded command — which looks exactly like a
        // QueryGuard bug until you count the registrations.
        builder.UseSetting("ConnectionStrings:Catalog", _connectionString);

        builder.ConfigureServices(services =>
        {
            // THE ONE PIECE OF SETUP THAT IS EASY TO GET WRONG.
            //
            // TestServer does not flow ExecutionContext into the request pipeline unless asked to, and
            // QueryGuard finds the active session through AsyncLocal. Without this, a scope opened in
            // the test is invisible to the interceptor running inside the request: the scope completes
            // with zero commands, and an assertion like `ExecutedQueryCount(1, result)` fails for a
            // reason that has nothing to do with the query count.
            //
            // It has to be configured here rather than by setting Server.PreserveExecutionContext after
            // CreateClient() — the flag is captured when the client's handler is built, so setting it
            // afterwards affects only the *next* client. That is a genuinely confusing failure: some
            // tests capture and some do not, depending on execution order.
            //
            // This is the documented AsyncLocal limitation from
            // docs/decisions/0002-session-propagation.md, showing up in the most likely place.
            services.Configure<Microsoft.AspNetCore.TestHost.TestServerOptions>(
                options => options.PreserveExecutionContext = true);

            // The middleware and an explicit scope both open sessions, and the innermost one wins. With
            // the middleware active it would open a session per request that shadows the test's scope,
            // so the scope would capture nothing. Turning it off makes the test's scope the only
            // session — which is how a real integration test using QueryGuard.Testing is set up.
            services.Configure<QueryGuardOptions>(options => options.Enabled = false);

            // A shared in-memory database disappears when the last connection to it closes, so one is
            // held open for the lifetime of the fixture.
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();

            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        });
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keepAlive?.Dispose();
            _keepAlive = null;
        }

        base.Dispose(disposing);
    }
}
