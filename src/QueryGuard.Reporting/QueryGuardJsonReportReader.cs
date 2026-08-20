using System;
using System.Globalization;
using System.Text.Json;

namespace QueryGuard.Reporting;

/// <summary>
/// Reads back the parts of a JSON report a baseline comparison needs.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a deserializer for <see cref="QueryGuardResult"/>. A result carries records,
/// findings, timings, and samples; a baseline needs a scope name and three counts. Reading only what is
/// needed means a report gaining a field cannot break this, and it keeps the reader from becoming a
/// second, subtly different definition of a result.
/// </para>
/// <para>
/// It lives here rather than in <c>QueryGuard.Core</c> because this assembly owns the report schema. A
/// reader in Core would mean Core depending on a format it does not define.
/// </para>
/// </remarks>
public static class QueryGuardJsonReportReader
{
    /// <summary>
    /// Reads a report into the entry a baseline stores.
    /// </summary>
    /// <param name="json">The document written by <see cref="QueryGuardJsonReporter"/>.</param>
    /// <returns>The entry describing what the report's scope cost.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="QueryGuardBaselineFormatException">
    /// The document is not a QueryGuard report, or its schema version is one this build cannot read.
    /// </exception>
    public static QueryGuardBaselineEntry ReadBaselineEntry(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            // Reusing the baseline exception type on purpose: to a caller, "this report cannot be read"
            // and "this baseline cannot be read" are the same class of problem — a file to go and fix.
            throw new QueryGuardBaselineFormatException("The report is not valid JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new QueryGuardBaselineFormatException("A QueryGuard report must be a JSON object.");
            }

            RequireReadableVersion(root);

            var scope = root.TryGetProperty("scope", out var scopeElement) ? scopeElement.GetString() : null;

            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new QueryGuardBaselineFormatException("The report has no 'scope'.");
            }

            if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
            {
                throw new QueryGuardBaselineFormatException("The report has no 'summary' object.");
            }

            return new QueryGuardBaselineEntry(
                scope,
                ReadCount(summary, "readCommands"),
                ReadCount(summary, "distinctQueries"),
                ReadTopOccurrences(root));
        }
    }

    /// <summary>
    /// Reports whether a document looks like a QueryGuard report at all.
    /// </summary>
    /// <param name="json">The document text.</param>
    /// <returns><see langword="true"/> when it carries a report's shape.</returns>
    /// <remarks>
    /// Used to skip unrelated JSON when a directory is scanned, rather than failing the whole run
    /// because a coverage file happened to be sitting next to the reports.
    /// </remarks>
    public static bool LooksLikeReport(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("schemaVersion", out _)
                && document.RootElement.TryGetProperty("scope", out _)
                && document.RootElement.TryGetProperty("summary", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads how many times the most repeated query ran.
    /// </summary>
    /// <remarks>
    /// The writer orders groups most-repeated first, so the head would do. This takes the maximum
    /// instead, because relying on the order would make a change to the writer's sort silently produce
    /// wrong baselines rather than a failing test.
    /// </remarks>
    private static int ReadTopOccurrences(JsonElement root)
    {
        if (!root.TryGetProperty("queryGroups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var top = 0;

        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind == JsonValueKind.Object
                && group.TryGetProperty("occurrences", out var occurrences)
                && occurrences.ValueKind == JsonValueKind.Number)
            {
                top = Math.Max(top, occurrences.GetInt32());
            }
        }

        return top;
    }

    private static void RequireReadableVersion(JsonElement root)
    {
        if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.String)
        {
            throw new QueryGuardBaselineFormatException(
                "The report has no schemaVersion. It was not written by QueryGuard.");
        }

        var text = version.GetString() ?? string.Empty;
        var major = text.Split('.')[0];

        if (!string.Equals(major, QueryGuardJsonReporter.SchemaVersion.Split('.')[0], StringComparison.Ordinal))
        {
            throw new QueryGuardBaselineFormatException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The report is schema version {text}; this build reads {QueryGuardJsonReporter.SchemaVersion}. Upgrade QueryGuard, or regenerate the reports."));
        }
    }

    private static int ReadCount(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            throw new QueryGuardBaselineFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The report summary is missing '{propertyName}'."));
        }

        return value.GetInt32();
    }
}
