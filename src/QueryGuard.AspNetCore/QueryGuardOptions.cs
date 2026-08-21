using System;
using System.Collections.Generic;

namespace QueryGuard.AspNetCore;

/// <summary>
/// Configures how QueryGuard behaves inside an ASP.NET Core application.
/// </summary>
public sealed class QueryGuardOptions
{
    private readonly Dictionary<string, QueryGuardPolicy> _endpointPolicies = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _excludedRoutePrefixes = ["/health", "/healthz", "/metrics", "/favicon.ico"];

    /// <summary>
    /// Gets or sets a value indicating whether QueryGuard observes requests at all.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The intended deployment is development and test environments. Enabling it in production is a
    /// deliberate choice, and the sample shows it gated on
    /// <c>builder.Environment.IsDevelopment()</c>. When disabled, the middleware opens no session, so
    /// the interceptor finds no scope and does nothing.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the policy applied to requests with no endpoint-specific policy.
    /// </summary>
    /// <remarks>
    /// The default warns rather than fails: installing QueryGuard must not break the first build it
    /// runs in. Making it fail is an explicit act.
    /// </remarks>
    public QueryGuardPolicy DefaultPolicy { get; set; } = QueryGuardPolicy.Create("default");

    /// <summary>
    /// Gets or sets what QueryGuard is allowed to retain about each command.
    /// </summary>
    /// <remarks>
    /// Defaults are privacy-first: no parameter values, no connection strings, no stack traces. See
    /// <c>docs/decisions/0004-parameter-privacy.md</c>.
    /// </remarks>
    public QueryGuardCaptureOptions Capture { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the per-request summary is logged even when no finding
    /// was produced. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// QueryGuard runs on every request, so logging a clean summary each time is noise that trains
    /// people to filter QueryGuard out. Useful when deliberately measuring, not as a default.
    /// </remarks>
    public bool LogSummaryWhenClean { get; set; }

    /// <summary>
    /// Gets the route prefixes QueryGuard ignores.
    /// </summary>
    /// <remarks>
    /// Health checks, metrics scrapes, and static files are polled constantly and say nothing about
    /// application query behavior. Pre-populated with the conventional paths; clear it to observe
    /// everything.
    /// </remarks>
    public IList<string> ExcludedRoutePrefixes => _excludedRoutePrefixes;

    /// <summary>
    /// Gets the policies registered for specific endpoints, keyed by route pattern.
    /// </summary>
    public IReadOnlyDictionary<string, QueryGuardPolicy> EndpointPolicies => _endpointPolicies;

    /// <summary>
    /// Registers a policy for one endpoint, starting from the default policy.
    /// </summary>
    /// <param name="routePattern">
    /// The endpoint's route pattern, for example <c>GET /api/reports/{id}</c>. This is the pattern, not
    /// a resolved URL; see <see cref="QueryGuardMiddleware"/>.
    /// </param>
    /// <param name="configure">Adjusts the policy for this endpoint.</param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="routePattern"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Starting from <see cref="DefaultPolicy"/> means an endpoint override adjusts the shared baseline
    /// rather than replacing it, so a capture setting or allowlist entry added to the default is not
    /// silently lost for every endpoint that has an override.
    /// </remarks>
    public QueryGuardOptions ForEndpoint(string routePattern, Func<QueryGuardPolicy, QueryGuardPolicy> configure)
    {
        if (string.IsNullOrWhiteSpace(routePattern))
        {
            throw new ArgumentException("A route pattern is required.", nameof(routePattern));
        }

        ArgumentNullException.ThrowIfNull(configure);

        _endpointPolicies[routePattern] = configure(DefaultPolicy.WithName(routePattern));
        return this;
    }

    /// <summary>
    /// Registers a ready-made policy for one endpoint.
    /// </summary>
    /// <param name="routePattern">The endpoint's route pattern.</param>
    /// <param name="policy">The policy.</param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="routePattern"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    public QueryGuardOptions ForEndpoint(string routePattern, QueryGuardPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(routePattern))
        {
            throw new ArgumentException("A route pattern is required.", nameof(routePattern));
        }

        ArgumentNullException.ThrowIfNull(policy);

        _endpointPolicies[routePattern] = policy;
        return this;
    }

    /// <summary>
    /// Resolves the policy for a route pattern.
    /// </summary>
    /// <param name="routePattern">The route pattern, or <see langword="null"/> when unmatched.</param>
    /// <returns>The endpoint policy if one is registered, otherwise the default renamed to the route.</returns>
    /// <remarks>
    /// Renaming the default is what makes a finding say which endpoint it came from without every
    /// endpoint needing its own registration.
    /// </remarks>
    public QueryGuardPolicy ResolvePolicy(string? routePattern)
    {
        if (routePattern is null)
        {
            return DefaultPolicy;
        }

        return _endpointPolicies.TryGetValue(routePattern, out var policy)
            ? policy
            : DefaultPolicy.WithName(routePattern);
    }

    /// <summary>
    /// Determines whether a request path is excluded from observation.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <returns><see langword="true"/> when the path should be ignored.</returns>
    public bool IsExcluded(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        for (var i = 0; i < _excludedRoutePrefixes.Count; i++)
        {
            if (path.StartsWith(_excludedRoutePrefixes[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
