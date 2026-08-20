using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace QueryGuard.Reporting;

/// <summary>
/// Renders a <see cref="QueryGuardResult"/> in some format.
/// </summary>
/// <remarks>
/// <para>
/// A reporter receives a result that has <em>already</em> been redacted, so it cannot emit a parameter
/// value or a connection string even if it tries. That is the point of centralizing redaction: adding
/// a reporter — including one a consumer writes — cannot introduce a leak. See
/// <c>docs/decisions/0004-parameter-privacy.md</c>.
/// </para>
/// <para>
/// Output must be deterministic for a given result. Two runs over the same data producing different
/// bytes would make snapshot tests useless and turn every CI diff into noise.
/// </para>
/// </remarks>
public interface IQueryGuardReporter
{
    /// <summary>
    /// Gets the conventional file extension for this format, including the leading dot.
    /// </summary>
    string FileExtension { get; }

    /// <summary>
    /// Renders a result to a string.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The rendered report.</returns>
    string Render(QueryGuardResult result);

    /// <summary>
    /// Writes a result to a stream.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="destination">The destination stream. Not disposed by this method.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the report has been written.</returns>
    Task WriteAsync(QueryGuardResult result, Stream destination, CancellationToken cancellationToken = default);
}
