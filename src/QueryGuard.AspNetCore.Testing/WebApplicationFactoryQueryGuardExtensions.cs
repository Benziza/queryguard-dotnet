using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueryGuard.EntityFrameworkCore;
using QueryGuard.Testing;

namespace QueryGuard.AspNetCore.Testing;

/// <summary>
/// Opens QueryGuard measurements around <see cref="WebApplicationFactory{TEntryPoint}"/> requests.
/// </summary>
public static class WebApplicationFactoryQueryGuardExtensions
{
    /// <summary>
    /// Creates a client and opens a QueryGuard scope that can observe its requests.
    /// </summary>
    /// <typeparam name="TEntry">The application entry point.</typeparam>
    /// <typeparam name="TContext">The EF Core context to observe.</typeparam>
    /// <param name="factory">The application factory.</param>
    /// <param name="name">The name shown in findings and reports.</param>
    /// <param name="policy">The query policy. Defaults to a policy with no budgets.</param>
    /// <returns>An open measurement and its configured HTTP client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public static QueryGuardWebApplicationMeasurement<TEntry> TrackQueries<
        TEntry,
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.NonPublicConstructors
            | DynamicallyAccessedMemberTypes.PublicProperties)] TContext>(
        this WebApplicationFactory<TEntry> factory,
        string name,
        QueryGuardPolicy? policy = null)
        where TEntry : class
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(factory);

        var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.Configure<TestServerOptions>(options => options.PreserveExecutionContext = true);
                services.Configure<QueryGuardOptions>(options => options.Enabled = false);
                services.TryAddSingleton<IQueryGuardSessionAccessor>(AsyncLocalQueryGuardSessionAccessor.Shared);

                services.AddDbContext<TContext>((provider, options) =>
                    options.UseQueryGuard(provider.GetRequiredService<IQueryGuardSessionAccessor>()));
            });
        });

        try
        {
            var client = configuredFactory.CreateClient();
            var accessor = configuredFactory.Services.GetRequiredService<IQueryGuardSessionAccessor>();
            var scope = QueryGuardScope.Start(name, policy, accessor);

            return new QueryGuardWebApplicationMeasurement<TEntry>(configuredFactory, client, scope);
        }
        catch
        {
            configuredFactory.Dispose();
            throw;
        }
    }
}
