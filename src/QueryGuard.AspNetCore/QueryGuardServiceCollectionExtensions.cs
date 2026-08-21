using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using QueryGuard.EntityFrameworkCore;

namespace QueryGuard.AspNetCore;

/// <summary>
/// Registers QueryGuard's services.
/// </summary>
public static class QueryGuardServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything QueryGuard needs, with privacy-first defaults.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Adoption friction is measured in lines of setup, so this is one call plus
    /// <c>app.UseQueryGuard()</c> plus attaching the interceptor to your <c>DbContext</c>. Everything
    /// registered here uses <c>TryAdd</c>, so an application that wants to substitute its own
    /// implementation registers it first and QueryGuard defers.
    /// </para>
    /// <para>
    /// The interceptor is a singleton because that is how EF Core treats it. It holds no request state.
    /// See <c>docs/decisions/0002-session-propagation.md</c>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddQueryGuard(options =>
    /// {
    ///     options.Enabled = builder.Environment.IsDevelopment();
    ///     options.DefaultPolicy = QueryGuardPolicy.Create("default")
    ///         .WithMaxQueries(20, QueryGuardSeverity.Warning)
    ///         .WithMaxOccurrencesPerFingerprint(5, QueryGuardSeverity.Failure);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddQueryGuard(
        this IServiceCollection services,
        Action<QueryGuardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<QueryGuardOptions>();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        // Fail at startup rather than at the first request. A misconfigured guard that only reveals
        // itself under load is worse than one that refuses to start.
        optionsBuilder.Validate(
            static options => options.DefaultPolicy is not null,
            "QueryGuardOptions.DefaultPolicy must not be null.");
        optionsBuilder.Validate(
            static options => options.Capture is not null,
            "QueryGuardOptions.Capture must not be null.");

        services.TryAddSingleton<IQueryGuardSessionAccessor, AsyncLocalQueryGuardSessionAccessor>();
        services.TryAddSingleton<ISqlNormalizer, SqlNormalizer>();
        services.TryAddSingleton<IQueryBudgetEvaluator, QueryBudgetEvaluator>();

        // Resolved from the options so that the capture settings an application configures are the ones
        // actually enforced. A copy is taken, so a later mutation cannot widen capture at runtime.
        services.TryAddSingleton<IQueryGuardRedactor>(provider =>
            new QueryGuardRedactor(provider.GetRequiredService<IOptions<QueryGuardOptions>>().Value.Capture));

        services.TryAddSingleton<IQueryFingerprintFactory>(provider => new QueryFingerprintFactory(
            provider.GetRequiredService<IQueryGuardRedactor>(),
            provider.GetRequiredService<ISqlNormalizer>()));

        services.TryAddSingleton(provider => new QueryGuardAnalyzer(
            provider.GetRequiredService<IQueryGuardRedactor>(),
            provider.GetRequiredService<IQueryBudgetEvaluator>()));

        // EF Core registers an interceptor as a singleton per DbContext configuration, so this matches
        // its lifetime. Attach it with `db.AddInterceptors(services.GetRequiredService<...>())`.
        services.TryAddSingleton<QueryGuardCommandInterceptor>();

        return services;
    }
}
