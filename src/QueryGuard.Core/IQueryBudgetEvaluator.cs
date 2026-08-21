using System.Collections.Generic;

namespace QueryGuard;

/// <summary>
/// Decides whether what a session did is acceptable, given its policy.
/// </summary>
/// <remarks>
/// <para>
/// Separate from grouping because they are different questions. Grouping asks "what happened?" and
/// has one right answer. Budgets ask "is that acceptable?" and the answer is whatever the user
/// configured, so the rules live behind a seam that a team with its own definition of acceptable can
/// replace.
/// </para>
/// <para>
/// Every rule an implementation adds should produce a finding carrying the numbers that justify it.
/// A verdict without its evidence is not something a reader can act on or disagree with.
/// </para>
/// </remarks>
public interface IQueryBudgetEvaluator
{
    /// <summary>
    /// Evaluates the session's policy against what it recorded.
    /// </summary>
    /// <param name="session">The completed session.</param>
    /// <param name="groups">The session's fingerprint groups, most repeated first.</param>
    /// <returns>
    /// The findings produced, in any order. The caller is responsible for ordering them
    /// deterministically.
    /// </returns>
    IReadOnlyList<QueryFinding> Evaluate(
        CompletedQueryGuardSession session,
        IReadOnlyList<QueryFingerprintGroup> groups);
}
