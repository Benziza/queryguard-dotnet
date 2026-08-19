using System;
using System.Buffers;
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
    /// The longest HTTP method accepted into a scope name.
    /// </summary>
    /// <remarks>
    /// The longest standard method is <c>PROPPATCH</c> at nine characters. Anything longer is not a
    /// method QueryGuard needs to name accurately, and a bounded length keeps a hostile request from
    /// padding a log line.
    /// </remarks>
    private const int MaxMethodLength = 16;

    /// <summary>
    /// Stands in for a request method that is not a plain HTTP token.
    /// </summary>
    private const string UnsafeMethod = "(method)";

    /// <summary>
    /// The characters an HTTP method may contain, as far as QueryGuard is concerned.
    /// </summary>
    /// <remarks>
    /// A narrowed version of the HTTP token grammar. Every standard method and every custom method
    /// anyone actually uses fits, and nothing that could break a log line does. Searched through
    /// <see cref="SearchValues"/> so the check is a vectorized span scan rather than a per-character
    /// loop — this runs once per request.
    /// </remarks>
    private static readonly SearchValues<char> MethodTokenCharacters = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.");

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

        // The route pattern comes from the application's own route table and is trusted. The method
        // comes from the request, and this name is written to logs — so a method containing a newline
        // could forge a log entry. Kestrel rejects such methods, but QueryGuard does not get to assume
        // which server it is hosted in.
        var method = SanitizeMethod(context.Request.Method);

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

    /// <summary>
    /// Reduces a request method to a plain HTTP token, or replaces it entirely.
    /// </summary>
    /// <remarks>
    /// An HTTP method is a token: letters, digits, and a small set of punctuation. Anything else is
    /// either a malformed request or an attempt to forge a log entry, and in both cases the exact
    /// characters are not worth reproducing. Rejecting the whole method rather than stripping the bad
    /// characters keeps two different hostile methods from collapsing into the same scope name.
    /// </remarks>
    private static string SanitizeMethod(string? method)
    {
        if (string.IsNullOrEmpty(method) || method.Length > MaxMethodLength)
        {
            return UnsafeMethod;
        }

        return method.AsSpan().ContainsAnyExcept(MethodTokenCharacters) ? UnsafeMethod : method;
    }
}
