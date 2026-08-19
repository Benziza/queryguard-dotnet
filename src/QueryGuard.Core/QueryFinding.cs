using System;
using System.Collections.Generic;

namespace QueryGuard;

/// <summary>
/// One piece of evidence produced by evaluating a completed session against a policy.
/// </summary>
/// <remarks>
/// <para>
/// A finding is evidence, not a verdict on the application's design. It carries the numbers that
/// justify it — occurrence counts, expected and actual values, timing, and a bounded SQL sample —
/// so that a reader can disagree with it on the facts.
/// </para>
/// <para>
/// A finding that is allowlisted is marked <see cref="IsIgnored"/> and keeps its
/// <see cref="IgnoreReason"/>. It is never removed: an allowlist that quietly deletes findings
/// becomes the place real problems go to die.
/// </para>
/// </remarks>
public sealed class QueryFinding
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueryFinding"/> class.
    /// </summary>
    /// <param name="kind">What kind of evidence this finding reports.</param>
    /// <param name="severity">How strongly QueryGuard reacts to it.</param>
    /// <param name="message">A self-contained, human-readable summary.</param>
    /// <param name="ruleName">The policy rule that produced it.</param>
    /// <param name="fingerprint">The fingerprint the finding concerns, when it concerns one.</param>
    /// <param name="expected">The configured limit, when the rule has one.</param>
    /// <param name="actual">The observed value, when the rule has one.</param>
    /// <param name="evidence">Supporting detail lines, already redacted.</param>
    /// <param name="isIgnored">Whether an allowlist entry matched this finding.</param>
    /// <param name="ignoreReason">The reason recorded on the matching allowlist entry.</param>
    /// <param name="stackTrace">A filtered first-occurrence stack trace, when capture is enabled.</param>
    /// <exception cref="ArgumentException"><paramref name="message"/> is empty or whitespace.</exception>
    public QueryFinding(
        QueryFindingKind kind,
        QueryGuardSeverity severity,
        string message,
        string ruleName,
        QueryFingerprint? fingerprint = null,
        long? expected = null,
        long? actual = null,
        IReadOnlyList<string>? evidence = null,
        bool isIgnored = false,
        string? ignoreReason = null,
        string? stackTrace = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A finding must explain itself.", nameof(message));
        }

        Kind = kind;
        Severity = severity;
        Message = message;
        RuleName = ruleName ?? string.Empty;
        Fingerprint = fingerprint;
        Expected = expected;
        Actual = actual;
        Evidence = evidence ?? Array.Empty<string>();
        IsIgnored = isIgnored;
        IgnoreReason = ignoreReason;
        StackTrace = stackTrace;
    }

    /// <summary>
    /// Gets what kind of evidence this finding reports.
    /// </summary>
    public QueryFindingKind Kind { get; }

    /// <summary>
    /// Gets how strongly QueryGuard reacts to this finding.
    /// </summary>
    public QueryGuardSeverity Severity { get; }

    /// <summary>
    /// Gets a self-contained, human-readable summary.
    /// </summary>
    /// <remarks>
    /// This message reaches the reader through a CI log or a test failure, usually with no
    /// documentation beside it, so it has to stand on its own.
    /// </remarks>
    public string Message { get; }

    /// <summary>
    /// Gets the name of the policy rule that produced this finding.
    /// </summary>
    public string RuleName { get; }

    /// <summary>
    /// Gets the fingerprint this finding concerns, or <see langword="null"/> for
    /// session-wide findings such as a total-count budget breach.
    /// </summary>
    public QueryFingerprint? Fingerprint { get; }

    /// <summary>
    /// Gets the configured limit, when the rule has one.
    /// </summary>
    public long? Expected { get; }

    /// <summary>
    /// Gets the observed value, when the rule has one.
    /// </summary>
    public long? Actual { get; }

    /// <summary>
    /// Gets supporting detail lines. Already redacted by the central privacy policy.
    /// </summary>
    public IReadOnlyList<string> Evidence { get; }

    /// <summary>
    /// Gets a value indicating whether an allowlist entry matched this finding.
    /// </summary>
    /// <remarks>
    /// An ignored finding does not affect <see cref="QueryGuardResult.IsSuccess"/>, but it stays
    /// in the report so a reviewer can ask whether its reason still holds.
    /// </remarks>
    public bool IsIgnored { get; }

    /// <summary>
    /// Gets the reason recorded on the matching allowlist entry, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Allowlist entries require a reason. That is what turns "turn this off" into "here is why
    /// this repetition is intentional", which is something a reviewer can evaluate.
    /// </remarks>
    public string? IgnoreReason { get; }

    /// <summary>
    /// Gets a filtered stack trace for the first occurrence, or <see langword="null"/> when
    /// capture is disabled.
    /// </summary>
    /// <remarks>
    /// Disabled by default, and bounded to one trace per fingerprint when enabled. See
    /// <c>docs/decisions/0007-stack-trace-policy.md</c>.
    /// </remarks>
    public string? StackTrace { get; }

    /// <summary>
    /// Gets a value indicating whether this finding causes the containing result to fail.
    /// </summary>
    public bool IsFailure => !IsIgnored && Severity == QueryGuardSeverity.Failure;

    /// <inheritdoc />
    public override string ToString()
    {
        var prefix = IsIgnored ? "[ignored] " : string.Empty;
        return $"{prefix}{Severity}: {Message}";
    }
}
