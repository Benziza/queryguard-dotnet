using System.Collections.Generic;

namespace QueryGuard;

/// <summary>
/// The single point through which anything QueryGuard is about to retain or emit must pass.
/// </summary>
/// <remarks>
/// <para>
/// Redaction is centralised rather than left to each reporter on purpose. A reporter that has to
/// remember to redact is a reporter that will eventually forget, and adding a new one would then be
/// a way to introduce a leak. With one redactor applied before a result is built, a reporter
/// receives data that is already safe and cannot opt out of that.
/// </para>
/// <para>
/// See <c>docs/decisions/0004-parameter-privacy.md</c>.
/// </para>
/// </remarks>
public interface IQueryGuardRedactor
{
    /// <summary>
    /// Gets the capture options this redactor enforces.
    /// </summary>
    QueryGuardCaptureOptions Options { get; }

    /// <summary>
    /// Redacts literals in a SQL statement and truncates it to the configured limit.
    /// </summary>
    /// <param name="sql">The SQL statement. May be <see langword="null"/>.</param>
    /// <returns>SQL safe to retain and to share, or an empty string when <paramref name="sql"/> is null.</returns>
    string RedactSql(string? sql);

    /// <summary>
    /// Filters framework frames out of a stack trace and truncates the result.
    /// </summary>
    /// <param name="stackTrace">The raw stack trace. May be <see langword="null"/>.</param>
    /// <returns>
    /// The application frames worth showing, or <see langword="null"/> when nothing is left to show.
    /// </returns>
    string? FilterStackTrace(string? stackTrace);

    /// <summary>
    /// Trims a sample collection to the configured per-fingerprint limit.
    /// </summary>
    /// <typeparam name="T">The sample type.</typeparam>
    /// <param name="samples">The candidate samples, in capture order.</param>
    /// <returns>At most <see cref="QueryGuardCaptureOptions.MaxSamplesPerFingerprint"/> samples.</returns>
    IReadOnlyList<T> LimitSamples<T>(IReadOnlyList<T> samples);
}
