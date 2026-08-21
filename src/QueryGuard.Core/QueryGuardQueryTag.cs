using System;
using System.Collections.Generic;

namespace QueryGuard;

/// <summary>
/// Recognizes QueryGuard directives that travel with a query as an EF Core tag.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's <c>TagWith</c> emits its argument as a leading SQL comment. That makes it the one place
/// a developer can attach an instruction to a specific LINQ query, right where the query is written,
/// rather than in a configuration file that drifts away from it.
/// </para>
/// <para>
/// Only the recognized <c>QueryGuard:</c> prefix is retained. An arbitrary tag is a comment like any
/// other and is stripped during normalization: QueryGuard does not keep text it was not asked to
/// interpret.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var items = await db.Items
///     .TagWith("QueryGuard:Ignore reason=bounded-reference-lookup")
///     .ToListAsync();
/// </code>
/// </example>
public static class QueryGuardQueryTag
{
    /// <summary>
    /// The prefix that marks a comment as a QueryGuard directive.
    /// </summary>
    public const string Prefix = "QueryGuard:";

    /// <summary>
    /// The directive that marks a query's repetition as intentional.
    /// </summary>
    /// <remarks>
    /// A finding matched by this directive is reported as <em>ignored</em>, with its reason, and is
    /// never removed. An allowlist that silently deletes findings becomes the place real problems go
    /// to die.
    /// </remarks>
    public const string IgnoreDirective = "QueryGuard:Ignore";

    /// <summary>
    /// Extracts every QueryGuard directive from a command's leading comments.
    /// </summary>
    /// <param name="commandText">The command text, which may be <see langword="null"/>.</param>
    /// <returns>
    /// The directives found, or <see langword="null"/> when there are none, which is the common case
    /// and avoids allocating an empty collection per command.
    /// </returns>
    /// <remarks>
    /// Only line comments are scanned, because that is what <c>TagWith</c> produces. Scanning stops
    /// at the first line that is neither blank nor a comment: a directive belongs at the top of the
    /// statement, and reading the whole statement on every command would be work done for nothing.
    /// </remarks>
    public static IReadOnlyList<string>? Extract(string? commandText)
    {
        if (string.IsNullOrEmpty(commandText))
        {
            return null;
        }

        // A statement without the prefix anywhere is the overwhelmingly common case, and this one
        // check keeps it to a single scan with no allocation.
        if (!commandText.Contains(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        List<string>? tags = null;
        var position = 0;

        while (position < commandText.Length)
        {
            var lineEnd = commandText.IndexOf('\n', position);
            var line = lineEnd < 0
                ? commandText.AsSpan(position)
                : commandText.AsSpan(position, lineEnd - position);

            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                position = Advance(lineEnd, commandText.Length);
                continue;
            }

            if (!trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                break;
            }

            var comment = trimmed[2..].Trim();
            if (comment.StartsWith(Prefix, StringComparison.Ordinal))
            {
                tags ??= [];
                tags.Add(comment.ToString());
            }

            position = Advance(lineEnd, commandText.Length);
        }

        return tags;
    }

    /// <summary>
    /// Determines whether a set of tags asks QueryGuard to treat the query's repetition as
    /// intentional.
    /// </summary>
    /// <param name="tags">The tags recognized on a command.</param>
    /// <returns><see langword="true"/> when an ignore directive is present.</returns>
    public static bool HasIgnoreDirective(IReadOnlyList<string>? tags)
    {
        if (tags is null)
        {
            return false;
        }

        for (var i = 0; i < tags.Count; i++)
        {
            if (tags[i].StartsWith(IgnoreDirective, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the reason a query's repetition was declared intentional.
    /// </summary>
    /// <param name="tags">The tags recognized on a command.</param>
    /// <returns>The reason text, or <see langword="null"/> when none was supplied.</returns>
    /// <remarks>
    /// The reason is what makes an ignore directive reviewable: "turn this off" is not something a
    /// reviewer can evaluate, but "bounded provider lookup, at most three sections" is.
    /// </remarks>
    public static string? GetIgnoreReason(IReadOnlyList<string>? tags)
    {
        if (tags is null)
        {
            return null;
        }

        const string ReasonKey = "reason=";

        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            if (!tag.StartsWith(IgnoreDirective, StringComparison.Ordinal))
            {
                continue;
            }

            var reasonStart = tag.IndexOf(ReasonKey, StringComparison.OrdinalIgnoreCase);
            if (reasonStart < 0)
            {
                continue;
            }

            var reason = tag[(reasonStart + ReasonKey.Length)..].Trim();
            if (reason.Length > 0)
            {
                return reason;
            }
        }

        return null;
    }

    private static int Advance(int lineEnd, int length) => lineEnd < 0 ? length : lineEnd + 1;
}
