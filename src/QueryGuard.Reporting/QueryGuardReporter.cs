using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QueryGuard.Reporting;

/// <summary>
/// Shared plumbing for the built-in reporters.
/// </summary>
/// <remarks>
/// Only the stream and file mechanics live here. Every reporter renders its own text, because a base
/// class that tried to share formatting between plain text, JSON, and XML would end up abstracting the
/// one thing each format needs to control.
/// </remarks>
public abstract class QueryGuardReporter : IQueryGuardReporter
{
    /// <summary>
    /// UTF-8 without a byte-order mark.
    /// </summary>
    /// <remarks>
    /// A BOM breaks XML parsers that expect a declaration first, and confuses tools that diff report
    /// files. Nothing consuming these formats wants one.
    /// </remarks>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public abstract string FileExtension { get; }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public abstract string Render(QueryGuardResult result);

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public async Task WriteAsync(
        QueryGuardResult result,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(destination);

        var bytes = Utf8NoBom.GetBytes(Render(result));
        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a result to a file, creating the directory if needed.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="path">The destination path.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file has been written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <remarks>
    /// The directory is created because the common destination is an artifacts folder that a CI job has
    /// not made yet, and failing on that would be a pointless obstacle in the middle of a test run.
    /// </remarks>
    public async Task WriteAsync(QueryGuardResult result, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A destination path is required.", nameof(path));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, Render(result), Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }
}
