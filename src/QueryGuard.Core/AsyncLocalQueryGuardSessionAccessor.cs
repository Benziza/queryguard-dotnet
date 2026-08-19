using System;
using System.Threading;

namespace QueryGuard;

/// <summary>
/// The default <see cref="IQueryGuardSessionAccessor"/>, backed by <see cref="AsyncLocal{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AsyncLocal{T}"/> flows with <c>ExecutionContext</c>, which is what makes an
/// <c>await</c> boundary and <c>Task.Run</c> fan-out inside a request land in the right session
/// without the application passing anything around.
/// </para>
/// <para>
/// The known limitation is the same one: work that deliberately suppresses context flow — through
/// <c>ExecutionContext.SuppressFlow</c>, a custom scheduler that does not capture context, or
/// fire-and-forget work started before the scope opened — will not be captured. That is documented
/// behavior rather than a defect, and it is the reason
/// <see cref="QueryGuardSession.DroppedRecordCount"/> exists.
/// </para>
/// <para>
/// Activations form an immutable linked chain rather than a mutable stack, so a parent session is
/// restored by pointing the accessor at the parent node. Nothing is shared or mutated between
/// concurrent flows.
/// </para>
/// </remarks>
public sealed class AsyncLocalQueryGuardSessionAccessor : IQueryGuardSessionAccessor
{
    private readonly AsyncLocal<Activation?> _current = new();

    private int _outOfOrderDisposalCount;

    /// <inheritdoc />
    public QueryGuardSession? Current => _current.Value?.Session;

    /// <summary>
    /// Gets how many activations were disposed out of order.
    /// </summary>
    /// <remarks>
    /// A non-zero value means a scope was disposed while one of its children was still active,
    /// which almost always means a missing <c>await</c> or a <c>using</c> that does not nest the way
    /// the code reads. The accessor recovers by restoring the disposed activation's parent, but the
    /// count is kept so the situation is detectable rather than silently producing wrong
    /// attribution.
    /// </remarks>
    internal int OutOfOrderDisposalCount => Volatile.Read(ref _outOfOrderDisposalCount);

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public IQueryGuardSessionActivation Activate(QueryGuardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var activation = new Activation(this, session, _current.Value);
        _current.Value = activation;
        return activation;
    }

    private void Release(Activation activation)
    {
        var current = _current.Value;

        if (ReferenceEquals(current, activation))
        {
            _current.Value = activation.Parent;
            return;
        }

        // The activation being disposed is not the innermost one. Walk the chain: if it is still in
        // there, a child outlived its parent and the nesting is broken. Restoring the disposed
        // activation's parent is the closest thing to correct, and the counter records that
        // something is wrong with the caller's scope structure.
        for (var node = current; node is not null; node = node.Parent)
        {
            if (ReferenceEquals(node, activation))
            {
                Interlocked.Increment(ref _outOfOrderDisposalCount);
                _current.Value = activation.Parent;
                return;
            }
        }

        // Not in the chain at all. Either this activation was already released, or disposal is
        // happening on a flow that never saw it — a fire-and-forget continuation, for instance.
        // Clearing the accessor here would silently stop capture for an unrelated scope, so the
        // safest action is none.
    }

    private sealed class Activation : IQueryGuardSessionActivation
    {
        private readonly AsyncLocalQueryGuardSessionAccessor _accessor;
        private bool _isDisposed;

        internal Activation(
            AsyncLocalQueryGuardSessionAccessor accessor,
            QueryGuardSession session,
            Activation? parent)
        {
            _accessor = accessor;
            Session = session;
            Parent = parent;
        }

        public QueryGuardSession Session { get; }

        internal Activation? Parent { get; }

        public void Dispose()
        {
            // Idempotent: a diagnostics handle must never be the reason an exception path throws a
            // second, more confusing exception.
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _accessor.Release(this);
        }
    }
}
