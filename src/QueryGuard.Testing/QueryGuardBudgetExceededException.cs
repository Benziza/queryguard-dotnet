using System;

namespace QueryGuard.Testing;

/// <summary>
/// Thrown when a query budget was not satisfied.
/// </summary>
/// <remarks>
/// <para>
/// A plain exception, deliberately. Every test framework reports an unexpected exception with its
/// message, so xUnit, NUnit, MSTest, and TUnit all render this failure without QueryGuard referencing
/// any of them, and installing <c>QueryGuard.Testing</c> does not drag a test framework into a
/// consumer's project. See <c>docs/decisions/0010-testing-api.md</c>.
/// </para>
/// <para>
/// The cost of that choice is that there is no framework-native formatting to lean on, which is why
/// <see cref="QueryGuardAssert"/> puts the whole evidence trail in <see cref="Exception.Message"/>.
/// </para>
/// </remarks>
public sealed class QueryGuardBudgetExceededException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardBudgetExceededException"/> class.
    /// </summary>
    /// <param name="message">The failure message, carrying the evidence.</param>
    /// <param name="result">The result that failed.</param>
    public QueryGuardBudgetExceededException(string message, QueryGuardResult result)
        : base(message)
        => Result = result;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardBudgetExceededException"/> class.
    /// </summary>
    /// <param name="message">The failure message.</param>
    public QueryGuardBudgetExceededException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardBudgetExceededException"/> class.
    /// </summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The inner exception.</param>
    public QueryGuardBudgetExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardBudgetExceededException"/> class.
    /// </summary>
    public QueryGuardBudgetExceededException()
        : base("A QueryGuard query budget was exceeded.")
    {
    }

    /// <summary>
    /// Gets the result that failed, so a test can inspect the findings programmatically instead of
    /// parsing the message.
    /// </summary>
    public QueryGuardResult? Result { get; }
}
