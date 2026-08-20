using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace QueryGuard;

/// <summary>
/// What every measured scope cost at a known-good point, so a change can be compared against it.
/// </summary>
/// <remarks>
/// <para>
/// A budget asks the user for a number they usually do not have. <c>WithMaxQueries(10)</c> requires
/// knowing that ten is right — and the honest answer, on an endpoint nobody has measured, is that
/// nobody knows. So the number gets guessed, set too high to be useful, or set too low and then raised
/// until the build goes green.
/// </para>
/// <para>
/// A baseline asks for nothing. It records what the code does today and reports what changed:
/// </para>
/// <code>
/// GET /api/companies
///   3 -> 51 queries
/// </code>
/// <para>
/// That needs no threshold and no judgement to read. It is also the shape of the actual event a user
/// cares about — not "this endpoint is over budget" but "this pull request changed this endpoint".
/// </para>
/// <para>
/// Committed to the repository as a file, so the comparison happens against the merge base rather than
/// against a database somebody has to run. See <c>docs/decisions/0013-baseline-storage.md</c>.
/// </para>
/// </remarks>
public sealed class QueryGuardBaseline
{
    /// <summary>
    /// The schema version of the baseline document.
    /// </summary>
    /// <remarks>
    /// A baseline is committed, so a file written by one version will be read by another. Additive
    /// fields bump the minor version; removing or repurposing one is breaking. Same contract as the
    /// JSON report — see <c>docs/decisions/0011-versioning.md</c>.
    /// </remarks>
    public const string SchemaVersion = "1.0";

    private readonly Dictionary<string, QueryGuardBaselineEntry> _entries;

    private QueryGuardBaseline(Dictionary<string, QueryGuardBaselineEntry> entries) => _entries = entries;

    /// <summary>
    /// Gets a baseline with no entries recorded.
    /// </summary>
    public static QueryGuardBaseline Empty { get; } = new(new Dictionary<string, QueryGuardBaselineEntry>(StringComparer.Ordinal));

    /// <summary>
    /// Gets the recorded entries, ordered by scope.
    /// </summary>
    /// <remarks>
    /// Ordered so the committed file has a stable diff. An unordered write would show every scope as
    /// changed whenever a dictionary rehashed, and a baseline whose diff is noise is a baseline nobody
    /// reviews.
    /// </remarks>
    public IReadOnlyList<QueryGuardBaselineEntry> Entries
        => [.. _entries.Values.OrderBy(entry => entry.Scope, StringComparer.Ordinal)];

    /// <summary>
    /// Gets how many scopes are recorded.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Finds the entry for a scope.
    /// </summary>
    /// <param name="scope">The scope name to look up.</param>
    /// <returns>The entry, or <see langword="null"/> when the scope has no baseline yet.</returns>
    public QueryGuardBaselineEntry? Find(string? scope)
        => scope is not null && _entries.TryGetValue(scope, out var entry) ? entry : null;

    /// <summary>
    /// Returns a baseline with this result recorded, replacing any existing entry for its scope.
    /// </summary>
    /// <param name="result">The result to record.</param>
    /// <returns>A new baseline; this instance is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public QueryGuardBaseline Record(QueryGuardResult result)
        => Record(QueryGuardBaselineEntry.FromResult(result));

    /// <summary>
    /// Returns a baseline with this entry recorded, replacing any existing entry for its scope.
    /// </summary>
    /// <param name="entry">The entry to record.</param>
    /// <returns>A new baseline; this instance is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    public QueryGuardBaseline Record(QueryGuardBaselineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var copy = new Dictionary<string, QueryGuardBaselineEntry>(_entries, StringComparer.Ordinal)
        {
            [entry.Scope] = entry,
        };

        return new QueryGuardBaseline(copy);
    }

    /// <summary>
    /// Renders the baseline as JSON.
    /// </summary>
    /// <returns>The document text, ending with a newline.</returns>
    /// <remarks>
    /// Indented and newline-terminated because this file is committed and reviewed by people. Written
    /// by hand rather than by serializing the type, for the same reason the JSON report is: serializing
    /// would make the file format an accident of the class layout, so renaming a property would break
    /// every baseline already committed.
    /// </remarks>
    public string ToJson()
    {
        using var buffer = new System.IO.MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", SchemaVersion);
            writer.WriteStartArray("scopes");

            foreach (var entry in Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("scope", entry.Scope);
                writer.WriteNumber("readCommands", entry.ReadCommands);
                writer.WriteNumber("distinctQueries", entry.DistinctQueries);
                writer.WriteNumber("topFingerprintOccurrences", entry.TopFingerprintOccurrences);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>
    /// Reads a baseline from JSON.
    /// </summary>
    /// <param name="json">The document text.</param>
    /// <returns>The baseline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="QueryGuardBaselineFormatException">
    /// The document is not valid JSON, or its schema version is one this build cannot read.
    /// </exception>
    public static QueryGuardBaseline FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            // Wrapped deliberately. A corrupt baseline is a configuration problem the user has to fix,
            // and a raw JsonException with a byte offset does not say which file is wrong.
            throw new QueryGuardBaselineFormatException(
                "The baseline document is not valid JSON.",
                exception);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new QueryGuardBaselineFormatException("The baseline document must be a JSON object.");
            }

            RequireReadableVersion(root);

            var baseline = Empty;

            if (root.TryGetProperty("scopes", out var scopes) && scopes.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in scopes.EnumerateArray())
                {
                    baseline = baseline.Record(ReadEntry(element));
                }
            }

            return baseline;
        }
    }

    /// <summary>
    /// Rejects a document written by a future major version rather than reading it wrong.
    /// </summary>
    private static void RequireReadableVersion(JsonElement root)
    {
        if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.String)
        {
            throw new QueryGuardBaselineFormatException(
                "The baseline document has no schemaVersion. It was not written by QueryGuard.");
        }

        var text = version.GetString() ?? string.Empty;
        var major = text.Split('.')[0];

        if (!string.Equals(major, SchemaVersion.Split('.')[0], StringComparison.Ordinal))
        {
            throw new QueryGuardBaselineFormatException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The baseline document is schema version {text}; this build reads {SchemaVersion}. Regenerate the baseline, or upgrade QueryGuard."));
        }
    }

    private static QueryGuardBaselineEntry ReadEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new QueryGuardBaselineFormatException("Every entry in 'scopes' must be a JSON object.");
        }

        var scope = element.TryGetProperty("scope", out var scopeElement) ? scopeElement.GetString() : null;

        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new QueryGuardBaselineFormatException("A baseline entry is missing its 'scope'.");
        }

        return new QueryGuardBaselineEntry(
            scope,
            ReadCount(element, "readCommands"),
            ReadCount(element, "distinctQueries"),
            ReadCount(element, "topFingerprintOccurrences"));
    }

    private static int ReadCount(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            throw new QueryGuardBaselineFormatException(
                string.Create(CultureInfo.InvariantCulture, $"A baseline entry is missing '{propertyName}'."));
        }

        return value.GetInt32();
    }
}
