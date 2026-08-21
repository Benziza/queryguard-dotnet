using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace QueryGuard.AspNetCore.Tests;

/// <summary>
/// The middleware, exercised through a real HTTP pipeline over a real SQLite database.
/// </summary>
/// <remarks>
/// Integration tests rather than unit tests against a mock pipeline. Whether a fake
/// <c>RequestDelegate</c> is wrapped correctly says nothing about whether an EF Core query executed
/// inside a real ASP.NET Core request lands in the right session, or whether the response a client
/// receives is unchanged.
/// </remarks>
public class QueryGuardMiddlewareTests
{
    [Fact]
    public async Task A_repeated_query_endpoint_produces_a_candidate_finding()
    {
        // Six companies, one child query each, plus the parent query: the shape QueryGuard exists for.
        await using var app = await SampleApplication.StartAsync();
        using var client = app.CreateClient();

        var response = await client.GetAsync(new Uri("/api/companies", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(app.Logs.HasEventId(QueryGuardEventIds.RepeatedQueryCandidate));

        var summary = Assert.Single(app.Logs.WithEventId(QueryGuardEventIds.RequestSummary));
        Assert.Contains("GET /api/companies", summary.Message, StringComparison.Ordinal);
        Assert.Contains("7 read queries", summary.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_corrected_endpoint_produces_no_finding()
    {
        await using var app = await SampleApplication.StartAsync();
        using var client = app.CreateClient();

        var response = await client.GetAsync(new Uri("/api/companies/projected", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(app.Logs.HasEventId(QueryGuardEventIds.RepeatedQueryCandidate));
    }

    [Fact]
    public async Task The_response_is_byte_identical_with_and_without_query_guard()
    {
        // The promise the whole product rests on: installing QueryGuard does not change what a client
        // receives. Asserted by running the same request through both pipelines and comparing.
        await using var guarded = await SampleApplication.StartAsync();
        await using var plain = await SampleApplication.StartAsync(withQueryGuard: false);

        using var guardedClient = guarded.CreateClient();
        using var plainClient = plain.CreateClient();

        var guardedResponse = await guardedClient.GetAsync(new Uri("/api/companies", UriKind.Relative));
        var plainResponse = await plainClient.GetAsync(new Uri("/api/companies", UriKind.Relative));

        Assert.Equal(plainResponse.StatusCode, guardedResponse.StatusCode);
        Assert.Equal(
            await plainResponse.Content.ReadAsStringAsync(),
            await guardedResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            plainResponse.Content.Headers.ContentType?.ToString(),
            guardedResponse.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task Nothing_is_added_to_the_response_headers()
    {
        // No X-QueryGuard-* headers, no diagnostics leaking to a client. See ADR-0006.
        await using var app = await SampleApplication.StartAsync();
        using var client = app.CreateClient();

        var response = await client.GetAsync(new Uri("/api/companies", UriKind.Relative));

        Assert.DoesNotContain(
            response.Headers.Concat(response.Content.Headers),
            header => header.Key.Contains("QueryGuard", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Nothing_is_added_to_the_response_body()
    {
        await using var app = await SampleApplication.StartAsync();
        using var client = app.CreateClient();

        var body = await client.GetStringAsync(new Uri("/api/companies", UriKind.Relative));

        Assert.DoesNotContain("QueryGuard", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("QG-FP-", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failing_endpoint_still_produces_a_report_and_still_fails()
    {
        // The session is completed in a finally, because the failing request is usually the
        // interesting one, and QueryGuard must not swallow the failure to get there. The endpoint's
        // single query produces no finding, so the clean summary has to be enabled to observe it.
        await using var app = await SampleApplication.StartAsync(options => options.LogSummaryWhenClean = true);
        using var client = app.CreateClient();

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.GetAsync(new Uri("/api/boom", UriKind.Relative)));

        var summary = Assert.Single(app.Logs.WithEventId(QueryGuardEventIds.RequestSummary));
        Assert.Contains("GET /api/boom", summary.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_exceeded_budget_is_logged_and_the_request_still_succeeds()
    {
        // In a request, a budget failure changes what is logged and nothing else. Failing a build is
        // what the testing API is for.
        await using var app = await SampleApplication.StartAsync(options =>
            options.DefaultPolicy = QueryGuardPolicy.Create("default").WithMaxQueries(2));
        using var client = app.CreateClient();

        var response = await client.GetAsync(new Uri("/api/companies", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(app.Logs.HasEventId(QueryGuardEventIds.BudgetExceeded));
    }

    [Fact]
    public async Task A_clean_request_logs_nothing_by_default()
    {
        // QueryGuard runs on every request. A clean summary each time is noise that trains people to
        // filter QueryGuard out of their logs entirely.
        await using var app = await SampleApplication.StartAsync();
        using var client = app.CreateClient();

        _ = await client.GetAsync(new Uri("/api/companies/1", UriKind.Relative));

        Assert.Empty(app.Logs.FromQueryGuard());
    }

    [Fact]
    public async Task A_clean_request_can_be_logged_on_request()
    {
        await using var app = await SampleApplication.StartAsync(options => options.LogSummaryWhenClean = true);
        using var client = app.CreateClient();

        _ = await client.GetAsync(new Uri("/api/companies/1", UriKind.Relative));

        var summary = Assert.Single(app.Logs.WithEventId(QueryGuardEventIds.RequestSummary));
        Assert.Equal(LogLevel.Information, summary.Level);
    }

    [Fact]
    public async Task A_route_with_a_parameter_is_named_by_its_pattern_not_its_url()
    {
        // Using the URL would create a separate policy and a separate report identity per identifier,
        // so a per-endpoint budget could never be configured and no two runs would be comparable.
        await using var app = await SampleApplication.StartAsync(options => options.LogSummaryWhenClean = true);
        using var client = app.CreateClient();

        _ = await client.GetAsync(new Uri("/api/companies/1", UriKind.Relative));
        _ = await client.GetAsync(new Uri("/api/companies/2", UriKind.Relative));

        var summaries = app.Logs.WithEventId(QueryGuardEventIds.RequestSummary).ToList();

        Assert.Equal(2, summaries.Count);
        Assert.All(summaries, entry =>
        {
            Assert.Contains("GET /api/companies/{id:int}", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("/api/companies/1", entry.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task An_endpoint_policy_overrides_the_default()
    {
        await using var app = await SampleApplication.StartAsync(options =>
        {
            options.DefaultPolicy = QueryGuardPolicy.Create("default").WithMaxQueries(100);
            options.ForEndpoint("GET /api/companies", policy => policy.WithMaxQueries(2));
        });
        using var client = app.CreateClient();

        _ = await client.GetAsync(new Uri("/api/companies", UriKind.Relative));

        Assert.True(app.Logs.HasEventId(QueryGuardEventIds.BudgetExceeded));
    }

    [Fact]
    public async Task An_endpoint_policy_starts_from_the_default_rather_than_replacing_it()
    {
        // An override adjusts the shared baseline. Otherwise an allowlist entry or a threshold added to
        // the default would be silently lost for every endpoint that has an override.
        await using var app = await SampleApplication.StartAsync(options =>
        {
            options.DefaultPolicy = QueryGuardPolicy.Create("default").WithRepeatedQueryThreshold(99);
            options.ForEndpoint("GET /api/companies", policy => policy.WithMaxQueries(100));
        });
        using var client = app.CreateClient();

        _ = await client.GetAsync(new Uri("/api/companies", UriKind.Relative));

        // The inherited threshold of 99 means the six repeated child queries produce no candidate.
        Assert.False(app.Logs.HasEventId(QueryGuardEventIds.RepeatedQueryCandidate));
    }

    [Fact]
    public async Task An_allowlisted_fingerprint_is_logged_as_ignored_with_its_reason()
    {
        await using var app = await SampleApplication.StartAsync();
        using var client = app.CreateClient();

        // The fingerprint is not known until it has been observed once, which is the realistic
        // workflow: run it, read the identifier out of the report, then document the exception.
        _ = await client.GetAsync(new Uri("/api/companies", UriKind.Relative));

        var candidate = Assert.Single(
            app.Logs.WithEventId(QueryGuardEventIds.RepeatedQueryCandidate),
            entry => entry.Message.Contains("QG-FP-", StringComparison.Ordinal));
        var fingerprintId = ExtractFingerprintId(candidate.Message);

        await using var allowlisted = await SampleApplication.StartAsync(options =>
            options.ForEndpoint(
                "GET /api/companies",
                policy => policy.AllowFingerprint(fingerprintId, "Bounded department lookup for a fixed company list.")));
        using var allowlistedClient = allowlisted.CreateClient();

        _ = await allowlistedClient.GetAsync(new Uri("/api/companies", UriKind.Relative));

        var ignored = Assert.Single(allowlisted.Logs.WithEventId(QueryGuardEventIds.FindingIgnored));
        Assert.Contains("Bounded department lookup", ignored.Message, StringComparison.Ordinal);
        Assert.False(allowlisted.Logs.HasEventId(QueryGuardEventIds.RepeatedQueryCandidate));
    }

    [Fact]
    public async Task Health_checks_are_ignored()
    {
        // Polled constantly and saying nothing about application query behavior.
        await using var app = await SampleApplication.StartAsync(options => options.LogSummaryWhenClean = true);
        using var client = app.CreateClient();

        _ = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Empty(app.Logs.FromQueryGuard());
    }

    [Fact]
    public async Task Disabling_query_guard_stops_all_observation()
    {
        await using var app = await SampleApplication.StartAsync(options =>
        {
            options.Enabled = false;
            options.LogSummaryWhenClean = true;
        });
        using var client = app.CreateClient();

        var response = await client.GetAsync(new Uri("/api/companies", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(app.Logs.FromQueryGuard());
    }

    [Fact]
    public async Task Concurrent_requests_to_different_routes_stay_isolated()
    {
        // One interceptor instance, one accessor, many requests. The counts differ per route on
        // purpose: identical counts would hide a leaked record.
        await using var app = await SampleApplication.StartAsync(options => options.LogSummaryWhenClean = true);
        using var client = app.CreateClient();

        var requests = new List<Task>();
        for (var i = 0; i < 8; i++)
        {
            requests.Add(client.GetAsync(new Uri("/api/companies", UriKind.Relative)));
            requests.Add(client.GetAsync(new Uri("/api/companies/projected", UriKind.Relative)));
            requests.Add(client.GetAsync(new Uri("/api/companies/1", UriKind.Relative)));
        }

        await Task.WhenAll(requests);

        var summaries = app.Logs.WithEventId(QueryGuardEventIds.RequestSummary).ToList();

        Assert.Equal(24, summaries.Count);

        // The repeated-query endpoint always sees exactly seven reads: one parent plus six children.
        // A leaked record from a concurrent request would move that number.
        var repeatedQueryRoute = summaries
            .Where(entry => entry.Message.Contains("QueryGuard GET /api/companies ->", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(8, repeatedQueryRoute.Count);
        Assert.All(repeatedQueryRoute, entry =>
            Assert.Contains("7 read queries", entry.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unmatched_request_is_named_once_rather_than_per_url()
    {
        // Otherwise a scanner probing random paths would fill a report with one scope per URL it tried.
        await using var app = await SampleApplication.StartAsync(options => options.LogSummaryWhenClean = true);
        using var client = app.CreateClient();

        _ = await client.GetAsync(new Uri("/nope/one", UriKind.Relative));
        _ = await client.GetAsync(new Uri("/nope/two", UriKind.Relative));

        var summaries = app.Logs.WithEventId(QueryGuardEventIds.RequestSummary).ToList();

        Assert.Equal(2, summaries.Count);
        Assert.All(summaries, entry =>
            Assert.Contains(QueryGuardRouteName.Unmatched, entry.Message, StringComparison.Ordinal));
    }

    private static string ExtractFingerprintId(string message)
    {
        const string Prefix = "QG-FP-";
        var start = message.IndexOf(Prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No fingerprint identifier in: {message}");

        return message.Substring(start, Prefix.Length + 8);
    }
}
