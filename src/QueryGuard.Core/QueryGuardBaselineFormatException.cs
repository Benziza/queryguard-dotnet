using System;

namespace QueryGuard;

/// <summary>
/// Thrown when a baseline document cannot be read.
/// </summary>
/// <remarks>
/// A distinct type so a caller can tell "the baseline file is wrong" — a configuration problem someone
/// has to fix — from "this scope has no baseline yet", which is normal and is reported by
/// <see cref="QueryGuardBaseline.Find"/> returning <see langword="null"/>.
/// </remarks>
public sealed class QueryGuardBaselineFormatException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardBaselineFormatException"/> class.
    /// </summary>
    public QueryGuardBaselineFormatException()
        : base("The baseline document could not be read.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardBaselineFormatException"/> class.
    /// </summary>
    /// <param name="message">What is wrong with the document.</param>
    public QueryGuardBaselineFormatException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardBaselineFormatException"/> class.
    /// </summary>
    /// <param name="message">What is wrong with the document.</param>
    /// <param name="innerException">The underlying parse failure.</param>
    public QueryGuardBaselineFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
