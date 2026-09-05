using System;

namespace QueryGuard.EntityFrameworkCore;

/// <summary>
/// Recognizes modification keywords at statement boundaries without treating literal content as SQL.
/// </summary>
internal static class SqlCommandClassifier
{
    internal static bool ContainsModification(string sql)
    {
        var index = 0;
        var statementStart = true;

        while (index < sql.Length)
        {
            var current = sql[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '-' && Peek(sql, index + 1) == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] is not '\r' and not '\n')
                {
                    index++;
                }

                continue;
            }

            if (current == '/' && Peek(sql, index + 1) == '*')
            {
                index = SkipBlockComment(sql, index);
                continue;
            }

            if (current == ';')
            {
                statementStart = true;
                index++;
                continue;
            }

            if (current == '\'')
            {
                index = SqlStringLiteralScanner.SingleQuotedEnd(sql, index);
            }
            else if (current is '"' or '`' or '[')
            {
                index = SkipDelimited(sql, index, current == '[' ? ']' : current);
            }
            else if (current == '$' && SqlStringLiteralScanner.TryDollarQuotedEnd(sql, index, out var end))
            {
                index = end;
            }
            else if (statementStart)
            {
                var start = index;
                while (index < sql.Length && IsIdentifierPart(sql[index]))
                {
                    index++;
                }

                if (IsModificationKeyword(sql.AsSpan(start, index - start)))
                {
                    return true;
                }

                if (index == start)
                {
                    index++;
                }
            }
            else
            {
                index++;
            }

            statementStart = false;
        }

        return false;
    }

    private static int SkipDelimited(string sql, int opening, char closing)
    {
        var index = opening + 1;
        while (index < sql.Length)
        {
            if (sql[index] == closing)
            {
                if (Peek(sql, index + 1) != closing)
                {
                    return index + 1;
                }

                index += 2;
                continue;
            }

            index++;
        }

        return sql.Length;
    }

    private static int SkipBlockComment(string sql, int opening)
    {
        var depth = 1;
        var index = opening + 2;
        while (index < sql.Length)
        {
            if (sql[index] == '/' && Peek(sql, index + 1) == '*')
            {
                depth++;
                index += 2;
            }
            else if (sql[index] == '*' && Peek(sql, index + 1) == '/')
            {
                index += 2;
                if (--depth == 0)
                {
                    return index;
                }
            }
            else
            {
                index++;
            }
        }

        return sql.Length;
    }

    private static char Peek(string sql, int index) => index < sql.Length ? sql[index] : '\0';

    private static bool IsIdentifierPart(char value)
        => char.IsAsciiLetterOrDigit(value) || value >= '\u0080' || value is '_' or '@' or '$' or '#';

    private static bool IsModificationKeyword(ReadOnlySpan<char> token)
        => token.Equals("INSERT", StringComparison.OrdinalIgnoreCase)
            || token.Equals("UPDATE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("DELETE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("MERGE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("CREATE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("ALTER", StringComparison.OrdinalIgnoreCase)
            || token.Equals("DROP", StringComparison.OrdinalIgnoreCase)
            || token.Equals("TRUNCATE", StringComparison.OrdinalIgnoreCase);
}
