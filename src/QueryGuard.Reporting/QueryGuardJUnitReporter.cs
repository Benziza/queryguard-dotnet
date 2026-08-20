using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;

namespace QueryGuard.Reporting;

/// <summary>
/// Renders a result as JUnit XML, so a budget failure appears in CI the way a failing test does.
/// </summary>
/// <remarks>
/// <para>
/// JUnit XML is the lowest common denominator that almost every CI system renders natively. Emitting it
/// means a query budget failure shows up in the same place as a failing unit test, with no plugin, no
/// dashboard, and nothing for a team to install.
/// </para>
/// <para>
/// The mapping is: one <c>testsuite</c> per scope, and one <c>testcase</c> per rule that was evaluated.
/// A satisfied rule is a passing case, a failure is a <c>failure</c>, a warning is a
/// <c>system-out</c> note on a passing case, and an ignored finding is a <c>skipped</c> case carrying
/// its reason. Warnings deliberately do not fail the suite — turning evidence into a red build by
/// default is how a tool gets switched off.
/// </para>
/// </remarks>
public sealed class QueryGuardJUnitReporter : QueryGuardReporter
{
    /// <inheritdoc />
    public override string FileExtension => ".xml";

    /// <inheritdoc />
    public override string Render(QueryGuardResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            Encoding = Encoding.UTF8,

            // The writer is handed a StringBuilder, so this keeps line endings identical on Windows
            // and Linux. Without it the same result renders differently per platform and a snapshot
            // test can only pass on one of them.
            NewLineChars = "\n",
            CloseOutput = false,
        };

        using (var writer = XmlWriter.Create(builder, settings))
        {
            WriteDocument(writer, result);
        }

        return builder.ToString();
    }

    private static void WriteDocument(XmlWriter writer, QueryGuardResult result)
    {
        var cases = BuildCases(result);

        var failures = 0;
        var skipped = 0;
        for (var i = 0; i < cases.Count; i++)
        {
            if (cases[i].Skipped is not null)
            {
                skipped++;
            }
            else if (cases[i].Failure is not null)
            {
                failures++;
            }
        }

        writer.WriteStartElement("testsuites");
        writer.WriteAttributeString("name", "QueryGuard");
        WriteCount(writer, "tests", cases.Count);
        WriteCount(writer, "failures", failures);
        WriteCount(writer, "skipped", skipped);

        writer.WriteStartElement("testsuite");
        writer.WriteAttributeString("name", result.SessionName);
        WriteCount(writer, "tests", cases.Count);
        WriteCount(writer, "failures", failures);
        WriteCount(writer, "skipped", skipped);
        WriteCount(writer, "errors", 0);
        writer.WriteAttributeString(
            "time",
            (result.TotalDatabaseDuration.TotalSeconds).ToString("F3", CultureInfo.InvariantCulture));
        writer.WriteAttributeString("timestamp", result.StartedAt.ToString("O", CultureInfo.InvariantCulture));

        WriteProperties(writer, result);

        for (var i = 0; i < cases.Count; i++)
        {
            WriteCase(writer, result, cases[i]);
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteProperties(XmlWriter writer, QueryGuardResult result)
    {
        writer.WriteStartElement("properties");

        WriteProperty(writer, "queryguard.policy", result.PolicyName);
        WriteProperty(writer, "queryguard.readCommands", result.ReadCommandCount.ToString(CultureInfo.InvariantCulture));
        WriteProperty(writer, "queryguard.distinctQueries", result.Groups.Count.ToString(CultureInfo.InvariantCulture));
        WriteProperty(
            writer,
            "queryguard.databaseMilliseconds",
            result.TotalDatabaseDuration.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture));

        writer.WriteEndElement();
    }

    private static void WriteProperty(XmlWriter writer, string name, string value)
    {
        writer.WriteStartElement("property");
        writer.WriteAttributeString("name", name);
        writer.WriteAttributeString("value", value);
        writer.WriteEndElement();
    }

    private static void WriteCase(XmlWriter writer, QueryGuardResult result, JUnitCase testCase)
    {
        writer.WriteStartElement("testcase");
        writer.WriteAttributeString("classname", result.SessionName);
        writer.WriteAttributeString("name", testCase.Name);
        writer.WriteAttributeString("time", "0.000");

        if (testCase.Skipped is { } skipped)
        {
            writer.WriteStartElement("skipped");
            writer.WriteAttributeString("message", skipped);
            writer.WriteEndElement();
        }
        else if (testCase.Failure is { } failure)
        {
            writer.WriteStartElement("failure");
            writer.WriteAttributeString("message", failure.Message);
            writer.WriteAttributeString("type", failure.Type);
            writer.WriteString(failure.Detail);
            writer.WriteEndElement();
        }

        if (testCase.SystemOut is { } systemOut)
        {
            writer.WriteStartElement("system-out");
            writer.WriteString(systemOut);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteCount(XmlWriter writer, string name, int value)
        => writer.WriteAttributeString(name, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Turns findings into test cases, with a passing case when nothing was reported.
    /// </summary>
    /// <remarks>
    /// A clean scope still emits one passing case. An empty suite renders as "no tests" in most CI
    /// viewers, which reads as "QueryGuard did not run" — the opposite of the truth.
    /// </remarks>
    private static List<JUnitCase> BuildCases(QueryGuardResult result)
    {
        var cases = new List<JUnitCase>(Math.Max(result.Findings.Count, 1));

        for (var i = 0; i < result.Findings.Count; i++)
        {
            var finding = result.Findings[i];
            var name = finding.Fingerprint is { } fingerprint
                ? string.Create(CultureInfo.InvariantCulture, $"{finding.RuleName} [{fingerprint.Id}]")
                : finding.RuleName;

            var detail = BuildDetail(finding);

            if (finding.IsIgnored)
            {
                cases.Add(new JUnitCase(
                    name,
                    Skipped: finding.IgnoreReason ?? "Ignored by an allowlist entry.",
                    Failure: null,
                    SystemOut: detail));
                continue;
            }

            if (finding.Severity == QueryGuardSeverity.Failure)
            {
                cases.Add(new JUnitCase(
                    name,
                    Skipped: null,
                    Failure: new JUnitFailure(finding.Message, finding.RuleName, detail),
                    SystemOut: null));
                continue;
            }

            // A warning is a passing case with its evidence attached. Failing the suite on a
            // repeated-query candidate by default would break the first build QueryGuard runs in.
            cases.Add(new JUnitCase(name, Skipped: null, Failure: null, SystemOut: detail));
        }

        if (cases.Count == 0)
        {
            cases.Add(new JUnitCase(
                "query-budget",
                Skipped: null,
                Failure: null,
                SystemOut: string.Create(
                    CultureInfo.InvariantCulture,
                    $"{result.ReadCommandCount} read queries in {result.Groups.Count} distinct queries; no findings.")));
        }

        return cases;
    }

    private static string BuildDetail(QueryFinding finding)
    {
        var builder = new StringBuilder(finding.Message);

        for (var i = 0; i < finding.Evidence.Count; i++)
        {
            builder.Append('\n').Append("  ").Append(finding.Evidence[i]);
        }

        if (finding.StackTrace is { } stackTrace)
        {
            builder.Append('\n').Append("  first occurrence at:").Append('\n').Append(stackTrace);
        }

        return builder.ToString();
    }

    private sealed record JUnitCase(string Name, string? Skipped, JUnitFailure? Failure, string? SystemOut);

    private sealed record JUnitFailure(string Message, string Type, string Detail);
}
