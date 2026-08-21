namespace QueryGuard;

/// <summary>
/// Provides the session that commands observed on the current asynchronous flow belong to.
/// </summary>
/// <remarks>
/// <para>
/// This interface exists because EF Core registers a <c>DbCommandInterceptor</c> as a
/// <strong>singleton</strong>. One interceptor instance sees commands from every concurrent
/// request, every parallel test, and every fan-out inside a single request. It therefore cannot
/// hold per-scope state: it has to ask where the command it is looking at belongs.
/// </para>
/// <para>
/// The default implementation is <see cref="AsyncLocalQueryGuardSessionAccessor"/>. Replacing it is
/// supported for a host with a better propagation mechanism, but any replacement must guarantee
/// the same thing: two concurrent flows never observe each other's session. Getting that wrong
/// corrupts every number QueryGuard reports, and it fails intermittently: the worst possible
/// failure mode for a tool whose whole purpose is to make test results trustworthy.
/// </para>
/// <para>
/// See <c>docs/decisions/0002-session-propagation.md</c>.
/// </para>
/// </remarks>
public interface IQueryGuardSessionAccessor
{
    /// <summary>
    /// Gets the session for the current asynchronous flow, or <see langword="null"/> when no scope
    /// is open.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> means <em>capture nothing</em>. QueryGuard stays silent rather than
    /// guessing which scope a command belongs to.
    /// </remarks>
    QueryGuardSession? Current { get; }

    /// <summary>
    /// Makes <paramref name="session"/> current for the calling flow and everything it awaits.
    /// </summary>
    /// <param name="session">The session to activate.</param>
    /// <returns>
    /// A handle that restores the previously current session when disposed. Disposing it is
    /// mandatory; the previous session is not restored otherwise.
    /// </returns>
    /// <remarks>
    /// Nesting is supported. Disposal restores the parent session rather than clearing the
    /// accessor, and it must do so on the exception path too: a scope that only unwinds cleanly
    /// on success corrupts every measurement taken after the first failing test.
    /// </remarks>
    IQueryGuardSessionActivation Activate(QueryGuardSession session);
}
