using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace QueryGuard.AspNetCore.Tests;

/// <summary>
/// Many concurrent requests through one pipeline, each reporting only its own queries.
/// </summary>
/// <remarks>
/// <para>
/// The interceptor is a stateless singleton and the session accessor is backed by
/// <c>AsyncLocal</c>. That design is only worth anything if it holds under load, and the failure it
/// would produce is the worst kind QueryGuard can have: numbers that look plausible and are wrong.
/// Nobody would question a report claiming 8 reads instead of 7 until they went looking for a
/// regression that was never really there.
/// </para>
/// <para>
/// So the routes exercised here have <em>deliberately different</em> read counts: 7, 3 and 1. If
/// every route ran the same number of queries, a record crossing between two requests would still
/// produce the expected totals everywhere and the bug would pass. Distinct counts are what make
/// leakage detectable rather than merely unlikely.
/// </para>
/// <para>
/// These tests are never retried. A flake here is a real defect in either the code or the test. See
/// <c>docs/testing-strategy.md</c>, and <c>SessionIsolationStressTests</c> in the core suite for the
/// same property asserted against explicit scopes rather than requests.
/// </para>
/// </remarks>
public class RequestIsolationStressTests
{
    /// <summary>
    /// Repeats of each stress test. A race that reproduces one time in three passes a single run.
    /// </summary>
    private const int Iterations = 3;

    /// <summary>
    /// Routes and the exact number of read queries each one performs, every time.
    /// </summary>
    private static readonly (string Route, int ReadQueries)[] Routes =
    [
        ("/api/companies", 7),
        ("/api/reports/rollup", 3),
        ("/api/companies/projected", 1),
    ];

    [Fact]
    public async Task Parallel_requests_across_routes_each_report_only_their_own_queries()
    {
        const int RequestsPerRoute = 12;

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            await using var app = await SampleApplication.StartAsync(options => options.LogSummaryWhenClean = true);
            using var client = app.CreateClient();

            var responses = await Task.WhenAll(
                Enumerable.Range(0, RequestsPerRoute)
                    .SelectMany(_ => Routes.Select(route => Get(client, route.Route))));

            Assert.All(responses, status => Assert.Equal(HttpStatusCode.OK, status));

            var summaries = Summaries(app);

            Assert.Equal(RequestsPerRoute * Routes.Length, summaries.Count);

            foreach (var (route, expectedReads) in Routes)
            {
                var forRoute = summaries.Where(summary => summary.Route == "GET " + route).ToList();

                Assert.Equal(RequestsPerRoute, forRoute.Count);

                // Every one of them, not the average and not most of them. A single leaked or lost
                // record shows up here as one wrong number among many right ones.
                Assert.All(forRoute, summary => Assert.Equal(expectedReads, summary.ReadQueries));
            }

            AssertNothingWasDroppedOrFailed(app);
        }
    }

    [Fact]
    public async Task Parallel_requests_to_one_route_do_not_accumulate_the_queries_of_the_others()
    {
        // The same route concurrently with itself is the case an AsyncLocal mistake breaks most
        // obviously: a session that was static, or scoped by accident, would grow request by request,
        // so the counts would climb rather than staying flat.
        const int Requests = 48;

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            await using var app = await SampleApplication.StartAsync();
            using var client = app.CreateClient();

            var responses = await Task.WhenAll(
                Enumerable.Range(0, Requests).Select(_ => Get(client, "/api/companies")));

            Assert.All(responses, status => Assert.Equal(HttpStatusCode.OK, status));

            var summaries = Summaries(app);

            Assert.Equal(Requests, summaries.Count);
            Assert.All(summaries, summary => Assert.Equal(7, summary.ReadQueries));

            AssertNothingWasDroppedOrFailed(app);
        }
    }

    [Fact]
    public async Task A_request_that_throws_does_not_contaminate_the_requests_beside_it()
    {
        // The failing request is the interesting one, and it is also the one whose session is
        // completed from a finally while its neighbours are mid-flight. If unwinding a session took a
        // shortcut, the damage would land on the healthy requests running alongside it.
        const int Rounds = 8;

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            await using var app = await SampleApplication.StartAsync(options => options.LogSummaryWhenClean = true);
            using var client = app.CreateClient();

            var work = new List<Task<HttpStatusCode>>(Rounds * 3);
            for (var round = 0; round < Rounds; round++)
            {
                work.Add(Get(client, "/api/companies"));
                work.Add(Get(client, "/api/boom"));
                work.Add(Get(client, "/api/reports/rollup"));
            }

            _ = await Task.WhenAll(work);

            var summaries = Summaries(app);

            var healthy = summaries.Where(summary => summary.Route == "GET /api/companies").ToList();
            var rollups = summaries.Where(summary => summary.Route == "GET /api/reports/rollup").ToList();
            var failures = summaries.Where(summary => summary.Route == "GET /api/boom").ToList();

            Assert.Equal(Rounds, healthy.Count);
            Assert.All(healthy, summary => Assert.Equal(7, summary.ReadQueries));

            Assert.Equal(Rounds, rollups.Count);
            Assert.All(rollups, summary => Assert.Equal(3, summary.ReadQueries));

            // The failing endpoint executes one query before it throws, and it is still reported.
            Assert.Equal(Rounds, failures.Count);
            Assert.All(failures, summary => Assert.Equal(1, summary.ReadQueries));

            AssertNothingWasDroppedOrFailed(app);
        }
    }

    private static async Task<HttpStatusCode> Get(HttpClient client, string route)
    {
        try
        {
            using var response = await client.GetAsync(new Uri(route, UriKind.Relative));
            return response.StatusCode;
        }
        catch (InvalidOperationException)
        {
            // TestServer rethrows an unhandled endpoint exception at the client. The failing route is
            // in the mix on purpose; what matters here is the report it produced, not the transport.
            return HttpStatusCode.InternalServerError;
        }
    }

    private static void AssertNothingWasDroppedOrFailed(SampleApplication app)
    {
        // A dropped record means a command completed after its session did, which under concurrency
        // would point at a session being unwound too early. A reporting failure means an exception was
        // swallowed on the way out, which would hide exactly the corruption being looked for.
        Assert.Empty(app.Logs.WithEventId(QueryGuardEventIds.RecordsDroppedAfterCompletion));
        Assert.Empty(app.Logs.WithEventId(QueryGuardEventIds.ReportingFailed));
    }

    private static List<RequestSummary> Summaries(SampleApplication app)
        => [.. app.Logs
            .WithEventId(QueryGuardEventIds.RequestSummary)
            .Select(entry => RequestSummary.Parse(entry.Message))];

    /// <summary>
    /// The route and read count carried by one summary log entry.
    /// </summary>
    /// <remarks>
    /// Read back out of the formatted message rather than through a test-only hook, because the log
    /// line <em>is</em> the middleware's observable output: the thing an operator actually reads.
    /// Parsing it means these tests also fail if its shape changes, which is the right place for that
    /// to surface.
    /// </remarks>
    private sealed record RequestSummary(string Route, int ReadQueries)
    {
        internal static RequestSummary Parse(string message)
        {
            const string RoutePrefix = "QueryGuard ";
            const string RouteSuffix = " -> ";
            const string ReadSuffix = " read queries";

            var routeStart = message.IndexOf(RoutePrefix, StringComparison.Ordinal) + RoutePrefix.Length;
            var routeEnd = message.IndexOf(RouteSuffix, routeStart, StringComparison.Ordinal);

            Assert.True(routeEnd > routeStart, $"Unexpected summary message shape: {message}");

            var readsEnd = message.IndexOf(ReadSuffix, routeEnd, StringComparison.Ordinal);

            Assert.True(readsEnd > routeEnd, $"Unexpected summary message shape: {message}");

            var readsStart = message.LastIndexOf(' ', readsEnd - 1) + 1;

            return new RequestSummary(
                message[routeStart..routeEnd],
                int.Parse(message[readsStart..readsEnd], CultureInfo.InvariantCulture));
        }
    }
}
