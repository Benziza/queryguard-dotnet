using System;
using System.Text;

namespace QueryGuard;

/// <summary>
/// The default <see cref="ISqlNormalizer"/>: a single conservative pass over the text.
/// </summary>
/// <remarks>
/// <para>
/// What it does, and nothing more:
/// </para>
/// <list type="number">
///   <item><description>collapses runs of whitespace, including line breaks, to a single space;</description></item>
///   <item><description>removes comments, except a recognized <c>QueryGuard:</c> directive;</description></item>
///   <item><description>replaces provider parameter references with a single placeholder;</description></item>
///   <item><description>drops the statement terminators and batch prologue some providers prepend.</description></item>
/// </list>
/// <para>
/// What it deliberately never does: reorder tokens, sort clauses, canonicalize aliases, rewrite
/// quoted identifiers, or attempt to recognize that two differently written statements are
/// semantically equivalent. Each of those would risk merging genuinely different queries, and a
/// report that points at the wrong SQL is worse than one that stays quiet.
/// </para>
/// <para>
/// String literals and quoted identifiers are copied through untouched. Redacting literals is the
/// redactor's job, applied after normalization, so there is exactly one place that decides what
/// counts as data.
/// </para>
/// </remarks>
public sealed class SqlNormalizer : ISqlNormalizer
{
    /// <summary>
    /// Replaces every provider parameter reference, whatever its syntax.
    /// </summary>
    public const string ParameterPlaceholder = "?";

    /// <summary>
    /// The placeholder as a character, so appends on the hot path stay allocation-free.
    /// </summary>
    private const char ParameterPlaceholderChar = '?';

    /// <inheritdoc />
    public string Normalize(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(commandText.Length);
        var index = 0;

        while (index < commandText.Length)
        {
            var current = commandText[index];

            if (char.IsWhiteSpace(current))
            {
                index = SkipWhitespace(commandText, index);
                AppendSeparator(builder);
                continue;
            }

            switch (current)
            {
                case '\'':
                    index = CopyStringLiteral(commandText, index, builder);
                    continue;

                case '"':
                    index = CopyDelimited(commandText, index, builder, '"');
                    continue;

                case '`':
                    index = CopyDelimited(commandText, index, builder, '`');
                    continue;

                case '[':
                    index = CopyDelimited(commandText, index, builder, ']');
                    continue;

                case '-' when Peek(commandText, index + 1) == '-':
                    index = HandleLineComment(commandText, index, builder);
                    continue;

                case '/' when Peek(commandText, index + 1) == '*':
                    index = HandleBlockComment(commandText, index, builder);
                    continue;

                case ':' when Peek(commandText, index + 1) == ':':
                    // PostgreSQL's cast operator. Without this, the second colon would look like the
                    // start of a named parameter and `"Id"::integer` would normalize to `"Id":?`,
                    // silently merging queries that differ by type.
                    builder.Append("::");
                    index += 2;
                    continue;

                case '@' or ':' or '$':
                    if (TryConsumeParameter(commandText, index, out var afterParameter))
                    {
                        builder.Append(ParameterPlaceholderChar);
                        index = afterParameter;
                        continue;
                    }

                    break;

                case '?':
                    // Already a positional placeholder; keep it in the same shape as the others.
                    builder.Append(ParameterPlaceholderChar);
                    index++;
                    continue;

                case ';':
                    // Statement terminators vary between providers and between EF Core versions for
                    // the same logical query, so they carry no information worth grouping on.
                    index++;
                    AppendSeparator(builder);
                    continue;

                default:
                    break;
            }

            builder.Append(current);
            index++;
        }

        return builder.ToString().Trim();
    }

    private static char Peek(string text, int index) => index < text.Length ? text[index] : '\0';

    private static void AppendSeparator(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != ' ')
        {
            builder.Append(' ');
        }
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static int CopyStringLiteral(string text, int index, StringBuilder builder)
    {
        var start = index;
        index++;

        while (index < text.Length)
        {
            if (text[index] == '\'')
            {
                // A doubled quote is an escaped quote inside the literal, not its end.
                if (Peek(text, index + 1) == '\'')
                {
                    index += 2;
                    continue;
                }

                index++;
                break;
            }

            index++;
        }

        builder.Append(text, start, index - start);
        return index;
    }

    private static int CopyDelimited(string text, int index, StringBuilder builder, char closing)
    {
        var start = index;
        index++;

        while (index < text.Length && text[index] != closing)
        {
            index++;
        }

        if (index < text.Length)
        {
            index++;
        }

        builder.Append(text, start, index - start);
        return index;
    }

    private static int HandleLineComment(string text, int index, StringBuilder builder)
    {
        var contentStart = index + 2;
        var end = text.IndexOf('\n', index);
        var contentEnd = end < 0 ? text.Length : end;

        var comment = text.AsSpan(contentStart, contentEnd - contentStart).Trim();

        if (comment.StartsWith(QueryGuardQueryTag.Prefix, StringComparison.Ordinal))
        {
            // A QueryGuard directive is the one comment that changes behavior, so it has to survive
            // normalization and therefore participates in the fingerprint. A tagged query is a
            // distinct call site the user chose to single out.
            AppendSeparator(builder);
            builder.Append("--").Append(comment);
            AppendSeparator(builder);
        }
        else
        {
            AppendSeparator(builder);
        }

        return end < 0 ? text.Length : end + 1;
    }

    private static int HandleBlockComment(string text, int index, StringBuilder builder)
    {
        var contentStart = index + 2;
        var closing = text.IndexOf("*/", contentStart, StringComparison.Ordinal);
        var contentEnd = closing < 0 ? text.Length : closing;

        var comment = text.AsSpan(contentStart, contentEnd - contentStart).Trim();

        if (comment.StartsWith(QueryGuardQueryTag.Prefix, StringComparison.Ordinal))
        {
            AppendSeparator(builder);
            builder.Append("/*").Append(comment).Append("*/");
        }

        AppendSeparator(builder);
        return closing < 0 ? text.Length : closing + 2;
    }

    /// <summary>
    /// Consumes a provider parameter reference if one starts at <paramref name="index"/>.
    /// </summary>
    /// <remarks>
    /// Covers the families EF Core providers actually emit: <c>@p0</c> and <c>@__city_0</c>
    /// (SQL Server, SQLite, MySQL), <c>$1</c> (PostgreSQL positional), and <c>:name</c> (Oracle and
    /// some Npgsql configurations). Normalizing these is not optional — without it, provider-generated
    /// identifiers alone would split a repeated query into N separate groups, which is precisely the
    /// case QueryGuard exists to find.
    /// </remarks>
    private static bool TryConsumeParameter(string text, int index, out int afterParameter)
    {
        afterParameter = index;

        var sigil = text[index];
        var position = index + 1;

        if (sigil == '$')
        {
            // PostgreSQL positional parameters are $1, $2, ... A bare $ is not a parameter.
            var digitsStart = position;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }

            if (position == digitsStart)
            {
                return false;
            }

            afterParameter = position;
            return true;
        }

        // @ and : are followed by an identifier. A lone sigil, or one followed by punctuation, is
        // something else entirely — PostgreSQL's :: cast operator, for instance.
        var nameStart = position;
        while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_'))
        {
            position++;
        }

        if (position == nameStart)
        {
            return false;
        }

        afterParameter = position;
        return true;
    }
}
