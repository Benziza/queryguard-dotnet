using System;

namespace QueryGuard;

/// <summary>
/// A handle that keeps one session current until it is disposed.
/// </summary>
/// <remarks>
/// Disposal restores whichever session was current before the activation, which is what makes
/// nested scopes work. Disposing twice is safe and does nothing the second time, because a
/// diagnostics handle must never be the reason an exception path fails.
/// </remarks>
public interface IQueryGuardSessionActivation : IDisposable
{
    /// <summary>
    /// Gets the session this activation made current.
    /// </summary>
    QueryGuardSession Session { get; }
}
