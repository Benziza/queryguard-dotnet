using System;

namespace QueryGuard;

/// <summary>
/// Finds literal boundaries shared by normalization and redaction.
/// </summary>
internal static class SqlStringLiteralScanner
{
    internal static int SingleQuotedEnd(string text, int openingQuote)
    {
        var escapeString = openingQuote > 0
            && text[openingQuote - 1] is 'E' or 'e'
            && (openingQuote == 1 || !IsIdentifierPart(text[openingQuote - 2]));
        var index = openingQuote + 1;

        while (index < text.Length)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                if (!escapeString && text[index + 1] == '\'')
                {
                    // Without a provider or SQL mode, this quote may close a standard SQL string
                    // or be escaped in MySQL. Retain neither interpretation's possible values:
                    // treat the rest as literal content, including any later strings.
                    return text.Length;
                }

                // E'...' has explicit PostgreSQL escape semantics. For ordinary strings, pairs
                // of backslashes do not change where a quote closes in either interpretation.
                if (escapeString || text[index + 1] == '\\')
                {
                    index += 2;
                    continue;
                }
            }

            if (text[index] == '\'')
            {
                if (index + 1 < text.Length && text[index + 1] == '\'')
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            index++;
        }

        return text.Length;
    }

    internal static bool TryDollarQuotedEnd(string text, int start, out int end)
    {
        end = start;
        if (start > 0 && IsIdentifierPart(text[start - 1]))
        {
            return false;
        }

        var position = start + 1;
        if (position < text.Length && text[position] != '$')
        {
            if (!IsTagStart(text[position]))
            {
                return false;
            }

            position++;
            while (position < text.Length && (IsTagStart(text[position]) || char.IsDigit(text[position])))
            {
                position++;
            }
        }

        if (position >= text.Length || text[position] != '$')
        {
            return false;
        }

        var delimiterLength = position - start + 1;
        var contentStart = position + 1;
        var delimiter = text.AsSpan(start, delimiterLength);
        var closingOffset = text.AsSpan(contentStart).IndexOf(delimiter, StringComparison.Ordinal);

        // An unterminated literal can arrive on the command-failure path. Its remaining content
        // is still private. Only the exact, case-sensitive opening delimiter can close it.
        end = closingOffset < 0 ? text.Length : contentStart + closingOffset + delimiterLength;
        return true;
    }

    // PostgreSQL accepts high-bit characters in unquoted identifiers and dollar-quote tags.
    private static bool IsTagStart(char value) => char.IsAsciiLetter(value) || value == '_' || value >= '\u0080';

    private static bool IsIdentifierPart(char value) => IsTagStart(value) || char.IsAsciiDigit(value) || value == '$';
}
