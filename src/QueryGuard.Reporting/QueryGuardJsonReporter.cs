using System;
using System.Globalization;
using System.Text.Json;

namespace QueryGuard.Reporting;

/// <summary>
/// Renders a result as JSON carrying an explicit schema version.
/// </summary>
/// <remarks>
/// <para>
/// Someone will build a dashboard on this output. A package version alone does not tell them whether
/// the shape they parse has changed, which is why the document carries its own
/// <see cref="SchemaVersion"/>. Additive fields bump the minor version; removing or repurposing a field
/// is a breaking change even in a preview and requires an ADR. See
/// <c>docs/decisions/0011-versioning.md</c>.
/// </para>
/// <para>
/// Written by hand with <see cref="Utf8JsonWriter"/> rather than by serializing the result object.
/// Serializing the model would make the wire format an accident of the type layout, so renaming an
/// internal property would silently break every consumer. Writing it out explicitly means the schema
/// changes only when someone edits this file.
/// </para>
/// </remarks>
public sealed class QueryGuardJsonReporter : QueryGuardReporter
{
    /// <summary>
    /// The schema version of the emitted document.
    /// </summary>
    public const string SchemaVersion = "1.0";

    private readonly bool _indented;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryGuardJsonReporter"/> class.
    /// </summary>
    /// <param name="indented">
    /// Whether to indent. Defaults to <see langword="true"/>, because the usual reader of this file is
    /// a person looking at a CI artifact.
    /// </param>
    public QueryGuardJsonReporter(bool indented = true) => _indented = indented;

    /// <inheritdoc />
    public override string FileExtension => ".json";

    /// <inheritdoc />
    public override string Render(QueryGuardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        using var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = _indented }))
        {
            WriteDocument(writer, result);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteDocument(Utf8JsonWriter writer, QueryGuardResult result)
    {
        writer.WriteStartObject();

        writer.WriteString("schemaVersion", SchemaVersion);
        writer.WriteString("scope", result.SessionName);
        writer.WriteString("policy", result.PolicyName);
        writer.WriteString("sessionId", result.SessionId.ToString("D", CultureInfo.InvariantCulture));

        // Round-trip format, so a consumer in another time zone reads the same instant.
        writer.WriteString("startedAt", result.StartedAt.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteBoolean("success", result.IsSuccess);

        WriteSummary(writer, result);
        WriteGroups(writer, result);
        WriteFindings(writer, result);

        writer.WriteEndObject();
    }

    private static void WriteSummary(Utf8JsonWriter writer, QueryGuardResult result)
    {
        writer.WriteStartObject("summary");
        writer.WriteNumber("totalCommands", result.TotalCommandCount);
        writer.WriteNumber("readCommands", result.ReadCommandCount);
        writer.WriteNumber("distinctQueries", result.Groups.Count);
        writer.WriteNumber("failures", result.FailureCount);
        writer.WriteNumber("warnings", result.WarningCount);
        writer.WriteNumber("ignored", result.IgnoredFindingCount);
        writer.WriteNumber("databaseMilliseconds", Round(result.TotalDatabaseDuration.TotalMilliseconds));
        writer.WriteNumber("elapsedMilliseconds", Round(result.Elapsed.TotalMilliseconds));
        writer.WriteEndObject();
    }

    private static void WriteGroups(Utf8JsonWriter writer, QueryGuardResult result)
    {
        writer.WriteStartArray("queryGroups");

        for (var i = 0; i < result.Groups.Count; i++)
        {
            var group = result.Groups[i];

            writer.WriteStartObject();
            writer.WriteString("fingerprint", group.Fingerprint.Id);
            writer.WriteNumber("occurrences", group.Occurrences);
            writer.WriteString("kind", group.Kind.ToString());
            writer.WriteNumber("databaseMilliseconds", Round(group.TotalDuration.TotalMilliseconds));
            writer.WriteNumber("firstSequence", group.FirstSequence);
            writer.WriteNumber("lastSequence", group.LastSequence);
            writer.WriteNumber("failures", group.FailureCount);

            // Already normalized and redacted before the fingerprint existed.
            writer.WriteString("sql", group.Fingerprint.NormalizedSql);

            WriteStringArray(writer, "tags", group.Tags);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteFindings(Utf8JsonWriter writer, QueryGuardResult result)
    {
        writer.WriteStartArray("findings");

        for (var i = 0; i < result.Findings.Count; i++)
        {
            var finding = result.Findings[i];

            writer.WriteStartObject();
            writer.WriteString("rule", finding.RuleName);
            writer.WriteString("kind", finding.Kind.ToString());
            writer.WriteString("severity", finding.Severity.ToString());
            writer.WriteString("message", finding.Message);

            if (finding.Fingerprint is { } fingerprint)
            {
                writer.WriteString("fingerprint", fingerprint.Id);
            }
            else
            {
                writer.WriteNull("fingerprint");
            }

            WriteNullableNumber(writer, "expected", finding.Expected);
            WriteNullableNumber(writer, "actual", finding.Actual);

            // Ignored findings are emitted, never dropped. A report that hides them turns an allowlist
            // into a blind spot.
            writer.WriteBoolean("ignored", finding.IsIgnored);

            if (finding.IgnoreReason is { } reason)
            {
                writer.WriteString("ignoreReason", reason);
            }
            else
            {
                writer.WriteNull("ignoreReason");
            }

            WriteStringArray(writer, "evidence", finding.Evidence);

            if (finding.StackTrace is { } stackTrace)
            {
                writer.WriteString("stackTrace", stackTrace);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        string propertyName,
        System.Collections.Generic.IReadOnlyList<string> values)
    {
        writer.WriteStartArray(propertyName);

        for (var i = 0; i < values.Count; i++)
        {
            writer.WriteStringValue(values[i]);
        }

        writer.WriteEndArray();
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, long? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(propertyName, number);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    /// <summary>
    /// Rounds a duration to a stable number of decimal places.
    /// </summary>
    /// <remarks>
    /// Without this, the full double precision of a measured duration would land in the output and no
    /// two runs would ever produce comparable JSON — every diff would be noise. Snapshot tests supply
    /// fixed durations, so this only affects real measurements.
    /// </remarks>
    private static double Round(double milliseconds) => Math.Round(milliseconds, 3, MidpointRounding.AwayFromZero);
}
