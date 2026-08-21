using System;
using System.Globalization;
using System.Text;

namespace QueryGuard;

/// <summary>
/// Renders the first useful application frame from a captured stack trace.
/// </summary>
internal static class QueryGuardOriginFormatter
{
    private const int MaxContextFrames = 3;

    /// <summary>
    /// Appends a readable origin and a small amount of calling context.
    /// </summary>
    internal static void Append(StringBuilder builder, string stackTrace, string indent)
    {
        var frames = stackTrace.Split('\n');
        var first = -1;

        for (var i = 0; i < frames.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(frames[i]))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            return;
        }

        builder
            .Append('\n')
            .Append(CultureInfo.InvariantCulture, $"{indent}origin: {Readable(frames[first])}");

        var context = 0;
        var previous = Callable(frames[first]);

        for (var i = first + 1; i < frames.Length && context < MaxContextFrames; i++)
        {
            if (string.IsNullOrWhiteSpace(frames[i]))
            {
                continue;
            }

            var callable = Callable(frames[i]);
            if (string.Equals(callable, previous, StringComparison.Ordinal))
            {
                continue;
            }

            previous = callable;
            builder
                .Append('\n')
                .Append(CultureInfo.InvariantCulture, $"{indent}    {frames[i].Trim()}");
            context++;
        }
    }

    private static string Readable(string frame)
    {
        var trimmed = frame.Trim();
        var callable = Callable(frame);
        var isCompilerGenerated = callable.Contains('<', StringComparison.Ordinal)
            || callable.Contains('>', StringComparison.Ordinal);

        if (!isCompilerGenerated)
        {
            return trimmed;
        }

        var inKeyword = trimmed.IndexOf(" in ", StringComparison.Ordinal);
        return inKeyword < 0 ? trimmed : trimmed[(inKeyword + 4)..].Trim();
    }

    private static string Callable(string frame)
    {
        var span = frame.AsSpan().Trim();

        if (span.StartsWith("at ", StringComparison.Ordinal))
        {
            span = span[3..];
        }

        var inKeyword = span.IndexOf(" in ", StringComparison.Ordinal);
        if (inKeyword >= 0)
        {
            span = span[..inKeyword];
        }

        return span.Trim().ToString();
    }
}
