using System;
using System.Threading;
using System.Threading.Tasks;

namespace QueryGuard.Testing;

/// <summary>
/// A QueryGuard session opened explicitly, for use in a test or any code that is not an HTTP request.
/// </summary>
/// <remarks>
/// <para>
/// This is the surface most users touch, because the main intended use of QueryGuard is inside an
/// integration test. Open a scope, exercise the code, complete the scope, assert on the result.
/// </para>
/// <para>
/// The scope is disposable both ways. <see cref="DisposeAsync"/> completes the session if the caller
/// did not, so a test that throws mid-way still releases the ambient session — otherwise the next test
/// on the same execution flow would record into a session nobody is reading.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// await using var scope = QueryGuardScope.Start(
///     "GET /api/companies",
///     QueryGuardPolicy.Create("companies").WithMaxQueries(3));
///
/// var response = await client.GetAsync("/api/companies");
///
/// QueryGuardAssert.Passes(await scope.CompleteAsync());
/// </code>
/// </example>
public sealed class QueryGuardScope : IDisposable, IAsyncDisposable
{
    private readonly IQueryGuardSessionActivation _activation;
    private readonly QueryGuardAnalyzer _analyzer;
    private QueryGuardResult? _result;
    private bool _isDisposed;

    private QueryGuardScope(
        QueryGuardSession session,
        IQueryGuardSessionActivation activation,
        QueryGuardAnalyzer analyzer)
    {
        Session = session;
        _activation = activation;
        _analyzer = analyzer;
    }

    /// <summary>
    /// Gets the session this scope opened.
    /// </summary>
    public QueryGuardSession Session { get; }

    /// <summary>
    /// Gets the default accessor, for wiring an interceptor when a test constructs one by hand.
    /// </summary>
    /// <remarks>
    /// The same instance as <see cref="AsyncLocalQueryGuardSessionAccessor.Shared"/>, which is what
    /// <c>UseQueryGuard()</c> attaches an interceptor to. Both defaults land here, so a scope opened
    /// without an accessor and an interceptor wired without one are already looking at each other.
    /// </remarks>
    public static IQueryGuardSessionAccessor DefaultAccessor => AsyncLocalQueryGuardSessionAccessor.Shared;

    /// <summary>
    /// Opens a scope.
    /// </summary>
    /// <param name="name">
    /// What this scope measures — a route pattern, or the name of the behavior under test. It appears in
    /// every finding and in the assertion message.
    /// </param>
    /// <param name="policy">The policy to evaluate. Defaults to a policy with no budgets.</param>
    /// <param name="accessor">
    /// The accessor the interceptor reads. Defaults to <see cref="DefaultAccessor"/>; pass the one from
    /// your service provider when the interceptor was resolved from it.
    /// </param>
    /// <param name="redactor">
    /// The capture settings to honour. Defaults to privacy-first defaults.
    /// </param>
    /// <param name="analyzer">The analyzer. Defaults to one built from <paramref name="redactor"/>.</param>
    /// <param name="captureOrigin">
    /// Whether to record where each distinct query was first executed, so a failure can name the call
    /// site instead of only the SQL. Defaults to <see langword="true"/> because a scope is a
    /// measurement, not a hot path. Ignored when <paramref name="redactor"/> is supplied.
    /// </param>
    /// <returns>The open scope.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public static QueryGuardScope Start(
        string name,
        QueryGuardPolicy? policy = null,
        IQueryGuardSessionAccessor? accessor = null,
        IQueryGuardRedactor? redactor = null,
        QueryGuardAnalyzer? analyzer = null,
        bool captureOrigin = true)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A scope name is required; it identifies the behavior under test in the failure message.",
                nameof(name));
        }

        // Stack-trace capture defaults ON here, unlike everywhere else.
        //
        // ADR-0007 turns it off by default because it costs 20-30x the rest of the capture path, and
        // that reasoning is about the request path in a running application. A scope exists only in a
        // test or an explicit measurement, where 150 microseconds is free and knowing the call site is
        // the difference between "this endpoint has a repeated query" and "line 87 has a repeated
        // query". Paying for it in the one place it is worth paying for is the whole point of having
        // the option.
        //
        // Still bounded to one trace per fingerprint, still filtered to application frames, and a
        // caller who supplies a redactor gets exactly what they asked for.
        var effectiveRedactor = redactor ?? new QueryGuardRedactor(new QueryGuardCaptureOptions
        {
            CaptureFirstStackTrace = captureOrigin,
        });
        var session = new QueryGuardSession(name, policy ?? QueryGuardPolicy.Create(name), effectiveRedactor);
        var effectiveAccessor = accessor ?? AsyncLocalQueryGuardSessionAccessor.Shared;

        return new QueryGuardScope(
            session,
            effectiveAccessor.Activate(session),
            analyzer ?? new QueryGuardAnalyzer(effectiveRedactor));
    }

    /// <summary>
    /// Completes the session and analyzes it.
    /// </summary>
    /// <returns>The result.</returns>
    /// <remarks>
    /// Idempotent: calling it again returns the same result rather than re-analyzing. That matters
    /// because <see cref="DisposeAsync"/> completes the scope too, and a test that both completes
    /// explicitly and disposes must not get two different answers.
    /// </remarks>
    public QueryGuardResult Complete() => _result ??= _analyzer.Analyze(Session.Complete());

    /// <summary>
    /// Completes the session and analyzes it.
    /// </summary>
    /// <param name="cancellationToken">Ignored; present so the API reads naturally in async tests.</param>
    /// <returns>A task producing the result.</returns>
    /// <remarks>
    /// Analysis is synchronous and in-memory — there is nothing to await. The asynchronous overload
    /// exists so that <c>await scope.CompleteAsync()</c> reads consistently with the surrounding async
    /// test code, and so the signature can become genuinely asynchronous later without a breaking
    /// change.
    /// </remarks>
    public ValueTask<QueryGuardResult> CompleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<QueryGuardResult>(Complete());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        // Completing here as well means a test that throws before completing still releases the
        // ambient session, instead of leaving the next test on this flow recording into a session
        // nobody will read.
        _ = Complete();
        _activation.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
}
