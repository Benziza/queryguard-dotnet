using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using QueryGuard.Testing;

namespace QueryGuard.AspNetCore.Testing;

/// <summary>
/// A QueryGuard measurement and the HTTP client whose requests it observes.
/// </summary>
/// <typeparam name="TEntry">The application entry point.</typeparam>
public sealed class QueryGuardWebApplicationMeasurement<TEntry> : IDisposable, IAsyncDisposable
    where TEntry : class
{
    private readonly WebApplicationFactory<TEntry> _factory;
    private readonly QueryGuardScope _scope;
    private bool _isDisposed;

    internal QueryGuardWebApplicationMeasurement(
        WebApplicationFactory<TEntry> factory,
        HttpClient client,
        QueryGuardScope scope)
    {
        _factory = factory;
        Client = client;
        _scope = scope;
    }

    /// <summary>
    /// Gets the client configured for this measurement.
    /// </summary>
    public HttpClient Client { get; }

    /// <summary>
    /// Completes the measurement and returns its result.
    /// </summary>
    /// <returns>The analyzed QueryGuard result.</returns>
    public QueryGuardResult Complete() => _scope.Complete();

    /// <summary>
    /// Completes the measurement and returns its result.
    /// </summary>
    /// <param name="cancellationToken">A token checked before completion.</param>
    /// <returns>The analyzed QueryGuard result.</returns>
    public ValueTask<QueryGuardResult> CompleteAsync(CancellationToken cancellationToken = default)
        => _scope.CompleteAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _scope.Dispose();
        Client.Dispose();
        _factory.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
}
