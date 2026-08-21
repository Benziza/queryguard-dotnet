using System;
using System.Globalization;

namespace QueryGuard;

/// <summary>
/// Where a query was executed from: the source file and line, parsed out of a captured stack trace.
/// </summary>
/// <remarks>
/// <para>
/// A captured trace is text, which is fine for printing and useless for anything that needs the file and
/// the line as separate values: SARIF wants a URI and an integer, not a sentence. This turns the first
/// application frame into those two values, or reports that it could not.
/// </para>
/// <para>
/// Frames arrive in the shape the runtime writes them:
/// </para>
/// <code>
/// at Sample.Api.CompanyEndpoints.List(AppDbContext db) in C:\repo\src\CompanyEndpoints.cs:line 89
/// </code>
/// <para>
/// A frame without the <c>in … :line N</c> suffix carries no location, which happens whenever the build
/// has no debug symbols. That is a normal state, not an error: the query still has an origin, there is
/// just nothing to point at. Callers are expected to omit the location rather than invent one, because a
/// wrong line number in an annotation is worse than no annotation.
/// </para>
/// <para>
/// It lives in this assembly rather than next to a consumer because both the test assertion and the
/// reporters need it, and the frame format is a property of what was captured.
/// </para>
/// </remarks>
public sealed class QueryGuardOrigin
{
    private const string InSeparator = " in ";
    private const string LineSeparator = ":line ";

    private QueryGuardOrigin(string callable, string filePath, int line)
    {
        Callable = callable;
        FilePath = filePath;
        Line = line;
    }

    /// <summary>
    /// Gets the method the query was executed from, without the file or line.
    /// </summary>
    /// <remarks>
    /// May be a compiler-generated name such as <c>Program.&lt;&lt;Main&gt;$&gt;b__0_3</c>. That is not
    /// worth showing a reader on its own, which is why <see cref="FilePath"/> and <see cref="Line"/> are
    /// the useful pair, but it is kept because it is the only thing available when there are no symbols.
    /// </remarks>
    public string Callable { get; }

    /// <summary>
    /// Gets the source file path exactly as the trace recorded it, which is absolute on most builds.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the one-based line number.
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Parses the first frame that carries a source location.
    /// </summary>
    /// <param name="stackTrace">A captured, already-filtered stack trace. May be <see langword="null"/>.</param>
    /// <param name="origin">The parsed origin, or <see langword="null"/> when there is none.</param>
    /// <returns><see langword="true"/> when a frame carried a file and a line.</returns>
    /// <remarks>
    /// The <em>first</em> frame with a location rather than the first frame: the nearest application
    /// frame is often a compiler-generated async state machine whose own frame has no symbols, while the
    /// frame just behind it does. Taking the first located frame lands on the code someone wrote.
    /// </remarks>
    public static bool TryParse(string? stackTrace, out QueryGuardOrigin? origin)
    {
        origin = null;

        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return false;
        }

        // Hand-rolled rather than ReadOnlySpan<char>.Split, which is .NET 9 and later; this assembly
        // also targets net8.0.
        var remaining = stackTrace.AsSpan();

        while (!remaining.IsEmpty)
        {
            var newline = remaining.IndexOf('\n');
            var frame = (newline < 0 ? remaining : remaining[..newline]).Trim();

            if (!frame.IsEmpty && TryParseFrame(frame, out origin))
            {
                return true;
            }

            if (newline < 0)
            {
                break;
            }

            remaining = remaining[(newline + 1)..];
        }

        return false;
    }

    /// <inheritdoc />
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{FilePath}:line {Line}");

    private static bool TryParseFrame(ReadOnlySpan<char> frame, out QueryGuardOrigin? origin)
    {
        origin = null;

        if (frame.StartsWith("at ", StringComparison.Ordinal))
        {
            frame = frame[3..];
        }

        var inIndex = frame.IndexOf(InSeparator, StringComparison.Ordinal);
        if (inIndex < 0)
        {
            return false;
        }

        var callable = frame[..inIndex].Trim();
        var location = frame[(inIndex + InSeparator.Length)..].Trim();

        // LastIndexOf, not IndexOf: a path is free to contain the separator, and the line number is
        // always last.
        var lineIndex = location.LastIndexOf(LineSeparator, StringComparison.Ordinal);
        if (lineIndex < 0)
        {
            return false;
        }

        var filePath = location[..lineIndex].Trim();
        var lineText = location[(lineIndex + LineSeparator.Length)..].Trim();

        if (filePath.IsEmpty
            || !int.TryParse(lineText, NumberStyles.None, CultureInfo.InvariantCulture, out var line)
            || line <= 0)
        {
            return false;
        }

        origin = new QueryGuardOrigin(callable.ToString(), filePath.ToString(), line);
        return true;
    }
}
