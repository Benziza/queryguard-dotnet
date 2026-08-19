using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace QueryGuard.AspNetCore;

/// <summary>
/// Names the scope a request's queries belong to.
/// </summary>
/// <remarks>
/// <para>
/// The name has to be the route <em>pattern</em>, not the resolved URL. Using the URL would create a
/// separate policy and a separate report identity for <c>/api/companies/1</c> and
/// <c>/api/companies/2</c>, so a per-endpoint budget could never be configured and no two runs would
/// ever be comparable.
/// </para>
/// <para>
/// It also has to be stable across restarts, so that a report from yesterday and one from today can be
/// compared.
/// </para>
/// </remarks>
public static class QueryGuardRouteName
{
    /// <summary>
    /// The name used when a request matched no endpoint.
    /// </summary>
    /// <remarks>
    /// A constant rather than the raw path: an unmatched request is usually a 404 probe, and using the
    /// path would let a scanner fill a report with one scope per URL it tried.
    /// </remarks>
    public const string Unmatched = "(unmatched)";

    /// <summary>
    /// Resolves the scope name for a request.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns>
    /// <c>METHOD /route/{pattern}</c> when the request matched a routed endpoint, the endpoint's
    /// display name when it has no route pattern, or <see cref="Unmatched"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public static string Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            return Unmatched;
        }

        var method = context.Request.Method;

        if (endpoint is RouteEndpoint routeEndpoint)
        {
            var pattern = routeEndpoint.RoutePattern.RawText;
            if (!string.IsNullOrEmpty(pattern))
            {
                // Normalized to a leading slash so `api/companies` and `/api/companies` cannot become
                // two different scopes for the same endpoint.
                return pattern.StartsWith('/')
                    ? $"{method} {pattern}"
                    : $"{method} /{pattern}";
            }
        }

        // A non-routed endpoint — a health check or a middleware-terminated branch — still has a
        // display name, which is better than falling back to the URL.
        return string.IsNullOrEmpty(endpoint.DisplayName)
            ? Unmatched
            : $"{method} {endpoint.DisplayName}";
    }
}
