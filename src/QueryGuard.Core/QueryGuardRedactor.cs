using System;
using System.Collections.Generic;
using System.Text;

namespace QueryGuard;

/// <summary>
/// The default <see cref="IQueryGuardRedactor"/>.
/// </summary>
/// <remarks>
/// <para>
/// The literal scanner is a deliberately small single pass over the text. It is not a SQL parser,
/// and it never tries to understand the statement: it recognises quoted strings, bare numbers, and
/// comments, and leaves everything else exactly as it was. A parser would be more precise and would
/// also be a project of its own, kept in step with every provider's SQL generation, with every gap
/// in it becoming a leak. See <c>docs/decisions/0005-sql-fingerprints.md</c>.
/// </para>
/// <para>
/// Redaction is idempotent: redacting already-redacted SQL leaves it unchanged, so a value cannot
/// reappear by passing through twice.
/// </para>
/// </remarks>
public sealed class QueryGuardRedactor : IQueryGuardRedactor
{
    /// <summary>
    /// Replaces a redacted literal. Chosen to be recognisable in a report and to be inert if the
    /// text is ever pasted back into a SQL client.
    /// </summary>
    public const string LiteralPlaceholder = "?";

    /// <summary>
    /// Appended when SQL is truncated, so a shortened statement is never mistaken for a whole one.
    /// </summary>
    public const string TruncationMarker = " /* … truncated by QueryGuard */";

    /// <summary>
    /// The placeholder as a character, so appends stay allocation-free.
    /// </summary>
    private const char LiteralPlaceholderChar = '?';

    private readonly QueryGuardCaptureOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardRedactor"/> class.
    /// </summary>
    /// <param name="options">
    /// The capture options to enforce. Copied, so a later mutation of the caller's instance cannot
    /// change capture behavior halfway through a request.
    /// </param>
    public QueryGuardRedactor(QueryGuardCaptureOptions? options = null)
        => _options = (options ?? new QueryGuardCaptureOptions()).Clone();

    /// <inheritdoc />
    public QueryGuardCaptureOptions Options => _options;

    /// <inheritdoc />
    public string RedactSql(string? sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return string.Empty;
        }

        var redacted = _options.RedactStringLiterals || _options.RedactNumericLiterals
            ? RedactLiterals(sql)
            : sql;

        return Truncate(redacted, _options.MaxNormalizedSqlLength);
    }

    /// <inheritdoc />
    public string? FilterStackTrace(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return null;
        }

        var builder = new StringBuilder();
        var kept = 0;
        var position = 0;

        // Walked as spans rather than split into an array: a stack trace is a few dozen lines and
        // this runs once per finding, so there is no reason to allocate a string per frame only to
        // discard most of them.
        while (position < stackTrace.Length)
        {
            var lineEnd = stackTrace.IndexOf('\n', position);
            var frame = lineEnd < 0
                ? stackTrace.AsSpan(position)
                : stackTrace.AsSpan(position, lineEnd - position);

            position = lineEnd < 0 ? stackTrace.Length : lineEnd + 1;

            frame = frame.TrimEnd('\r');
            if (frame.IsEmpty || IsFilteredFrame(frame))
            {
                continue;
            }

            if (kept > 0)
            {
                builder.Append('\n');
            }

            builder.Append(frame);
            kept++;
        }

        // Filtering everything away leaves an empty trace, which is worse than no trace: it looks
        // like capture is broken. Returning null says "nothing to show" unambiguously.
        return kept == 0 ? null : Truncate(builder.ToString(), _options.MaxNormalizedSqlLength);
    }

    /// <inheritdoc />
    public IReadOnlyList<T> LimitSamples<T>(IReadOnlyList<T> samples)
    {
        if (samples is null || samples.Count == 0)
        {
            return Array.Empty<T>();
        }

        var limit = _options.MaxSamplesPerFingerprint;
        if (limit == 0)
        {
            return Array.Empty<T>();
        }

        if (samples.Count <= limit)
        {
            return samples;
        }

        var trimmed = new T[limit];
        for (var i = 0; i < limit; i++)
        {
            trimmed[i] = samples[i];
        }

        return trimmed;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength), TruncationMarker);

    private bool IsFilteredFrame(ReadOnlySpan<char> frame)
    {
        // Frames look like "   at Some.Namespace.Type.Method(...) in file:line 42". Skip the
        // leading "at " so a filter prefix is matched against the namespace itself.
        var span = frame.TrimStart();
        if (span.StartsWith("at ", StringComparison.Ordinal))
        {
            span = span[3..];
        }

        var filters = _options.StackTraceFrameFilters;
        for (var i = 0; i < filters.Count; i++)
        {
            var filter = filters[i];
            if (!string.IsNullOrEmpty(filter) && span.StartsWith(filter, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return IsGeneratedFrame(span);
    }

    /// <summary>
    /// Reports whether a frame belongs to generated code rather than to anything a developer wrote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A namespace prefix filter cannot catch these, because they have no namespace. EF Core executes
    /// a compiled query through a dynamic method, and the nearest frame to the interceptor is:
    /// </para>
    /// <code>
    /// at lambda_method39(Closure, QueryContext)
    /// </code>
    /// <para>
    /// True, and useless: it was the first thing the origin line reported before this existed. Every
    /// frame a developer can act on is <c>Namespace.Type.Method(…)</c>, so a callable name with no dot
    /// in it is generated by definition.
    /// </para>
    /// </remarks>
    private static bool IsGeneratedFrame(ReadOnlySpan<char> frame)
    {
        var openParen = frame.IndexOf('(');
        var callable = openParen < 0 ? frame : frame[..openParen];

        return !callable.Contains('.');
    }

    /// <summary>
    /// Replaces quoted string literals and bare numeric literals with a placeholder.
    /// </summary>
    /// <remarks>
    /// Single-pass and intentionally conservative. Quoted identifiers: <c>"Departments"</c> in
    /// SQLite and PostgreSQL, <c>[Departments]</c> in SQL Server, and <c>`Departments`</c> in MySQL, are
    /// left alone, because a table name is structure rather than data and removing it would make the
    /// evidence unreadable. Doubled quotes inside a string literal are handled as escapes.
    /// </remarks>
    private string RedactLiterals(string sql)
    {
        var builder = new StringBuilder(sql.Length);
        var index = 0;

        while (index < sql.Length)
        {
            var current = sql[index];

            switch (current)
            {
                case '\'':
                    index = AppendStringLiteral(sql, index, builder);
                    continue;

                case '-' when index + 1 < sql.Length && sql[index + 1] == '-':
                    // Line comment: copied verbatim. Comment stripping belongs to the fingerprint
                    // normalizer, and doing it here as well would mean two places deciding which
                    // comments are semantic.
                    index = AppendUntilLineEnd(sql, index, builder);
                    continue;

                case '/' when index + 1 < sql.Length && sql[index + 1] == '*':
                    index = AppendBlockComment(sql, index, builder);
                    continue;

                case '"':
                    index = AppendQuotedIdentifier(sql, index, builder, '"');
                    continue;

                case '`':
                    index = AppendQuotedIdentifier(sql, index, builder, '`');
                    continue;

                case '[':
                    index = AppendQuotedIdentifier(sql, index, builder, ']');
                    continue;

                default:
                    if (_options.RedactNumericLiterals && IsNumericLiteralStart(sql, index))
                    {
                        index = SkipNumericLiteral(sql, index);
                        builder.Append(LiteralPlaceholderChar);
                        continue;
                    }

                    builder.Append(current);
                    index++;
                    continue;
            }
        }

        return builder.ToString();
    }

    private int AppendStringLiteral(string sql, int index, StringBuilder builder)
    {
        var start = index;
        index++; // opening quote

        while (index < sql.Length)
        {
            if (sql[index] == '\'')
            {
                // A doubled quote is an escaped quote inside the literal, not the end of it.
                if (index + 1 < sql.Length && sql[index + 1] == '\'')
                {
                    index += 2;
                    continue;
                }

                index++;
                break;
            }

            index++;
        }

        if (_options.RedactStringLiterals)
        {
            builder.Append('\'').Append(LiteralPlaceholderChar).Append('\'');
        }
        else
        {
            builder.Append(sql, start, index - start);
        }

        return index;
    }

    private static int AppendQuotedIdentifier(string sql, int index, StringBuilder builder, char closingChar)
    {
        var start = index;
        index++; // opening delimiter

        while (index < sql.Length && sql[index] != closingChar)
        {
            index++;
        }

        if (index < sql.Length)
        {
            index++; // closing delimiter
        }

        builder.Append(sql, start, index - start);
        return index;
    }

    private static int AppendUntilLineEnd(string sql, int index, StringBuilder builder)
    {
        var start = index;
        while (index < sql.Length && sql[index] != '\n')
        {
            index++;
        }

        builder.Append(sql, start, index - start);
        return index;
    }

    private static int AppendBlockComment(string sql, int index, StringBuilder builder)
    {
        var start = index;
        index += 2; // opening /*

        while (index + 1 < sql.Length && !(sql[index] == '*' && sql[index + 1] == '/'))
        {
            index++;
        }

        index = index + 1 < sql.Length ? index + 2 : sql.Length;
        builder.Append(sql, start, index - start);
        return index;
    }

    /// <summary>
    /// Determines whether position <paramref name="index"/> begins a numeric literal.
    /// </summary>
    /// <remarks>
    /// A digit only starts a literal when the preceding character cannot be part of an identifier.
    /// Without that check, the <c>1</c> in the EF Core alias <c>t1</c> or the parameter <c>@p0</c>
    /// would be replaced, which would corrupt the statement rather than protect anything.
    /// </remarks>
    private static bool IsNumericLiteralStart(string sql, int index)
    {
        if (!char.IsAsciiDigit(sql[index]))
        {
            return false;
        }

        if (index == 0)
        {
            return true;
        }

        var previous = sql[index - 1];
        return !char.IsAsciiLetterOrDigit(previous)
            && previous is not '_' and not '@' and not ':' and not '$' and not '#' and not '.';
    }

    private static int SkipNumericLiteral(string sql, int index)
    {
        while (index < sql.Length && (char.IsAsciiDigit(sql[index]) || sql[index] == '.'))
        {
            index++;
        }

        // Consume an exponent such as 1.5e-3 so the remainder is not mistaken for an identifier.
        if (index < sql.Length && (sql[index] is 'e' or 'E'))
        {
            var exponent = index + 1;
            if (exponent < sql.Length && sql[exponent] is '+' or '-')
            {
                exponent++;
            }

            if (exponent < sql.Length && char.IsAsciiDigit(sql[exponent]))
            {
                index = exponent;
                while (index < sql.Length && char.IsAsciiDigit(sql[index]))
                {
                    index++;
                }
            }
        }

        return index;
    }
}
