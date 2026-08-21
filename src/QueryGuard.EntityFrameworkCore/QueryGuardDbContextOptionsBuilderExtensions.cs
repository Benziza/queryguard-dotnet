using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace QueryGuard.EntityFrameworkCore;

/// <summary>
/// Attaches QueryGuard to a <see cref="DbContext"/> in one call.
/// </summary>
/// <remarks>
/// <para>
/// Before this existed, capturing a single query took three correct decisions: construct the
/// interceptor, hand it a fingerprint factory, and make sure its session accessor was the same
/// instance the scope would use. Getting the third one wrong produced the least helpful failure
/// available: the scope completed with zero commands, so an assertion about query counts failed for
/// a reason that had nothing to do with query counts.
/// </para>
/// <para>
/// This removes the decision. <c>UseQueryGuard()</c> binds to
/// <see cref="AsyncLocalQueryGuardSessionAccessor.Shared"/>, which is the same accessor
/// <c>QueryGuardScope.Start</c> uses by default, so the two are wired to each other by construction.
/// </para>
/// </remarks>
public static class QueryGuardDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Records the commands this context executes into whichever QueryGuard scope is open.
    /// </summary>
    /// <param name="builder">The options builder being configured.</param>
    /// <param name="sessionAccessor">
    /// The accessor to read. Defaults to <see cref="AsyncLocalQueryGuardSessionAccessor.Shared"/>,
    /// which is what <c>QueryGuardScope.Start</c> uses when it is not given one. Pass a container's
    /// accessor only when the scope will use that same one.
    /// </param>
    /// <param name="fingerprintFactory">
    /// Turns command text into a fingerprint. Defaults to <see cref="QueryFingerprintFactory"/> with
    /// privacy-first capture settings.
    /// </param>
    /// <returns>The same builder, so calls chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// options.UseSqlite(connectionString).UseQueryGuard();
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UseQueryGuard(
        this DbContextOptionsBuilder builder,
        IQueryGuardSessionAccessor? sessionAccessor = null,
        IQueryFingerprintFactory? fingerprintFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Attaching twice doubles every recorded command, and the resulting report looks plausible:
        // counts are exactly 2x, which reads as an application problem rather than a wiring one. It
        // is an easy mistake to make, because an application that calls AddQueryGuard() already has
        // an interceptor registered and adding this line looks like it should be harmless. So this
        // is a no-op when a QueryGuard interceptor is already attached.
        if (HasQueryGuardInterceptor(builder))
        {
            return builder;
        }

        var interceptor = new QueryGuardCommandInterceptor(
            sessionAccessor ?? AsyncLocalQueryGuardSessionAccessor.Shared,
            fingerprintFactory ?? new QueryFingerprintFactory());

        return builder.AddInterceptors(interceptor);
    }

    /// <summary>
    /// Records the commands this context executes into whichever QueryGuard scope is open.
    /// </summary>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <param name="builder">The options builder being configured.</param>
    /// <param name="sessionAccessor">The accessor to read. Defaults to the shared accessor.</param>
    /// <param name="fingerprintFactory">Turns command text into a fingerprint.</param>
    /// <returns>The same builder, so calls chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static DbContextOptionsBuilder<TContext> UseQueryGuard<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        IQueryGuardSessionAccessor? sessionAccessor = null,
        IQueryFingerprintFactory? fingerprintFactory = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        UseQueryGuard((DbContextOptionsBuilder)builder, sessionAccessor, fingerprintFactory);
        return builder;
    }

    /// <summary>
    /// Reports whether a QueryGuard interceptor is already attached to these options.
    /// </summary>
    private static bool HasQueryGuardInterceptor(DbContextOptionsBuilder builder)
    {
        var extension = builder.Options.FindExtension<CoreOptionsExtension>();

        IEnumerable<IInterceptor>? interceptors = extension?.Interceptors;

        return interceptors is not null
            && interceptors.Any(static interceptor => interceptor is QueryGuardCommandInterceptor);
    }
}
