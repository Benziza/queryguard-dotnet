using System;
using Microsoft.AspNetCore.Builder;

namespace QueryGuard.AspNetCore;

/// <summary>
/// Adds QueryGuard to the request pipeline.
/// </summary>
public static class QueryGuardApplicationBuilderExtensions
{
    /// <summary>
    /// Observes each request with QueryGuard.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <strong>Placement matters.</strong> Call this <em>after</em> <c>UseRouting</c>, because the scope
    /// name comes from the matched endpoint's route pattern. Called earlier, no endpoint is matched yet
    /// and every request lands in a single unmatched scope: QueryGuard still works, but every report
    /// loses the one label that makes it useful.
    /// </para>
    /// <para>
    /// The middleware only observes. It never writes to the response, adds headers, or throws, so its
    /// position relative to other middleware cannot change what a client receives.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// app.UseRouting();
    /// app.UseQueryGuard();
    /// app.MapControllers();
    /// </code>
    /// </example>
    public static IApplicationBuilder UseQueryGuard(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<QueryGuardMiddleware>();
    }
}
