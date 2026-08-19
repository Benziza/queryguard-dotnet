using System;
using System.Collections.Generic;

namespace QueryGuard;

/// <summary>
/// Controls exactly how much QueryGuard retains about each observed command.
/// </summary>
/// <remarks>
/// <para>
/// These defaults are part of the package's public contract, not a convenience. QueryGuard output
/// is meant to be shared — pasted into a pull request, uploaded as a CI artifact, attached to a
/// GitHub issue — so anything it captures should be assumed to end up somewhere public. Every
/// default here is the least it can retain while still producing usable evidence.
/// </para>
/// <para>
/// Changing a default is an ADR-level decision. See
/// <c>docs/decisions/0004-parameter-privacy.md</c>.
/// </para>
/// </remarks>
public sealed class QueryGuardCaptureOptions
{
    /// <summary>
    /// The default number of sample records retained per fingerprint group.
    /// </summary>
    public const int DefaultMaxSamplesPerFingerprint = 3;

    /// <summary>
    /// The default maximum length of retained normalized SQL, in characters.
    /// </summary>
    public const int DefaultMaxNormalizedSqlLength = 4096;

    /// <summary>
    /// Namespace prefixes filtered out of a captured stack trace by default, leaving the
    /// application frames that are actually actionable.
    /// </summary>
    private static readonly string[] DefaultFrameFilters =
    [
        "QueryGuard.",
        "Microsoft.EntityFrameworkCore.",
        "Microsoft.Data.",
        "Microsoft.AspNetCore.",
        "Npgsql.",
        "System.",
        "Xunit.",
    ];

    private int _maxSamplesPerFingerprint = DefaultMaxSamplesPerFingerprint;
    private int _maxNormalizedSqlLength = DefaultMaxNormalizedSqlLength;

    /// <summary>
    /// Gets or sets a value indicating whether parameter <em>values</em> are captured.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enabling this puts real user data into every report QueryGuard produces, including any you
    /// then attach to a public issue or a CI artifact. It exists because a repeated query executed
    /// with 51 <em>different</em> key values is much stronger evidence of an N+1 than the same query
    /// executed 51 times — but that is a trade you have to make deliberately.
    /// </para>
    /// <para>
    /// Parameter <em>names</em> are never retained either way; only the count is.
    /// </para>
    /// </remarks>
    public bool CaptureParameterValues { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether one filtered stack trace is captured for the first
    /// occurrence of each fingerprint. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// "Where is this coming from?" is the first question anyone asks after seeing a finding, and a
    /// stack trace is the only way to answer it from inside an interceptor. It is also expensive on
    /// a per-command path, which is why it is off by default and bounded to one trace per
    /// fingerprint when enabled. There is deliberately no option that captures a trace per command.
    /// See <c>docs/decisions/0007-stack-trace-policy.md</c>.
    /// </remarks>
    public bool CaptureFirstStackTrace { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether string literals surviving normalization are
    /// redacted. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// A query built by string concatenation rather than parameters carries its values inline. Left
    /// alone they would reach reports, so a query written unsafely does not also become a data leak.
    /// </remarks>
    public bool RedactStringLiterals { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether numeric literals surviving normalization are
    /// redacted. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// An inlined number is far more often an identifier than a page size, so the default treats it
    /// as data. The trade-off is real: redacting numbers also merges queries that differ only by a
    /// literal such as <c>LIMIT 10</c> versus <c>LIMIT 100</c>. Turn this off if that distinction
    /// matters more to you than the inlined values.
    /// </remarks>
    public bool RedactNumericLiterals { get; set; } = true;

    /// <summary>
    /// Gets or sets how many sample records are retained per fingerprint group.
    /// Defaults to <see cref="DefaultMaxSamplesPerFingerprint"/>.
    /// </summary>
    /// <remarks>
    /// Bounded so that an endpoint running one query ten thousand times does not cause QueryGuard to
    /// retain ten thousand records. Set to zero to keep counts and timing but no samples.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int MaxSamplesPerFingerprint
    {
        get => _maxSamplesPerFingerprint;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "The sample limit cannot be negative.");
            }

            _maxSamplesPerFingerprint = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum length of retained normalized SQL, in characters.
    /// Defaults to <see cref="DefaultMaxNormalizedSqlLength"/>.
    /// </summary>
    /// <remarks>
    /// Generated SQL for a wide projection can run to tens of kilobytes. Truncation keeps a report
    /// readable and keeps retention bounded, and it is always marked explicitly so nobody mistakes
    /// a truncated statement for the whole one.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    public int MaxNormalizedSqlLength
    {
        get => _maxNormalizedSqlLength;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "Retained SQL must be allowed at least one character.");
            }

            _maxNormalizedSqlLength = value;
        }
    }

    /// <summary>
    /// Gets the namespace prefixes filtered out of a captured stack trace.
    /// </summary>
    /// <remarks>
    /// Pre-populated with QueryGuard, EF Core, ASP.NET Core, the BCL, and common provider and test
    /// framework namespaces, so what is left is application code. Clear it to keep every frame.
    /// </remarks>
    public IList<string> StackTraceFrameFilters { get; } = [.. DefaultFrameFilters];

    /// <summary>
    /// Creates a copy of these options.
    /// </summary>
    /// <returns>An independent copy.</returns>
    /// <remarks>
    /// Options are configured once at startup and then read on the command path. Taking a copy at
    /// registration means a later mutation of the configuration object cannot change capture
    /// behavior halfway through a request.
    /// </remarks>
    public QueryGuardCaptureOptions Clone()
    {
        var copy = new QueryGuardCaptureOptions
        {
            CaptureParameterValues = CaptureParameterValues,
            CaptureFirstStackTrace = CaptureFirstStackTrace,
            RedactStringLiterals = RedactStringLiterals,
            RedactNumericLiterals = RedactNumericLiterals,
            MaxSamplesPerFingerprint = MaxSamplesPerFingerprint,
            MaxNormalizedSqlLength = MaxNormalizedSqlLength,
        };

        copy.StackTraceFrameFilters.Clear();
        for (var i = 0; i < StackTraceFrameFilters.Count; i++)
        {
            copy.StackTraceFrameFilters.Add(StackTraceFrameFilters[i]);
        }

        return copy;
    }
}
