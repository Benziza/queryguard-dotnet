using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using QueryGuard.AspNetCore.Testing.TestApp;
using QueryGuard.Testing;
using Xunit;

namespace QueryGuard.AspNetCore.Testing.Tests;

public class WebApplicationFactoryQueryGuardExtensionsTests
{
    [Fact]
    public async Task The_helper_attaches_queryguard_to_an_unconfigured_context()
    {
        using var factory = new WebApplicationFactory<Program>();
        await using var guard = factory.TrackQueries<Program, PlainCatalogDbContext>(
            "GET /plain/widgets",
            QueryGuardPolicy.Create("plain").WithMaxQueries(1));

        using var response = await guard.Client.GetAsync(new Uri("/plain/widgets", UriKind.Relative));
        var result = await guard.CompleteAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, result.ReadCommandCount);
        QueryGuardAssert.Passes(result);
    }

    [Fact]
    public async Task The_helper_does_not_double_count_an_existing_interceptor()
    {
        using var factory = new WebApplicationFactory<Program>();
        await using var guard = factory.TrackQueries<Program, GuardedCatalogDbContext>(
            "GET /guarded/widgets",
            QueryGuardPolicy.Create("guarded").WithMaxQueries(1));

        using var response = await guard.Client.GetAsync(new Uri("/guarded/widgets", UriKind.Relative));
        var result = await guard.CompleteAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, result.ReadCommandCount);
        QueryGuardAssert.Passes(result);
    }
}
