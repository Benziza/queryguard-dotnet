using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace QueryGuard.Reporting.Tests;

public class QueryGuardJsonReporterTests
{
    private readonly QueryGuardJsonReporter _reporter = new();

    [Fact]
    public void A_result_is_required()
        => Assert.Throws<ArgumentNullException>(() => _reporter.Render(null!));

    [Fact]
    public void The_document_declares_its_schema_version()
    {
        // Someone will build a dashboard on this. A package version alone does not tell them whether
        // the shape they parse has changed.
        var document = Parse(_reporter.Render(ReportFixture.FailingResult()));

        Assert.Equal(QueryGuardJsonReporter.SchemaVersion, document.RootElement.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public void The_document_is_valid_json_and_carries_the_scope_identity()
    {
        var document = Parse(_reporter.Render(ReportFixture.FailingResult()));
        var root = document.RootElement;

        Assert.Equal("GET /api/companies", root.GetProperty("scope").GetString());
        Assert.Equal("companies", root.GetProperty("policy").GetString());
        Assert.Equal(ReportFixture.FixedSessionId.ToString("D"), root.GetProperty("sessionId").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void The_summary_reports_the_numbers_a_consumer_would_chart()
    {
        var summary = Parse(_reporter.Render(ReportFixture.FailingResult())).RootElement.GetProperty("summary");

        Assert.Equal(52, summary.GetProperty("readCommands").GetInt32());
        Assert.Equal(2, summary.GetProperty("distinctQueries").GetInt32());
        Assert.Equal(1, summary.GetProperty("failures").GetInt32());
        Assert.Equal(1, summary.GetProperty("warnings").GetInt32());
        Assert.Equal(0, summary.GetProperty("ignored").GetInt32());
    }

    [Fact]
    public void Query_groups_are_emitted_most_repeated_first_with_their_sql()
    {
        var groups = Parse(_reporter.Render(ReportFixture.FailingResult())).RootElement.GetProperty("queryGroups");

        Assert.Equal(2, groups.GetArrayLength());

        var first = groups[0];
        Assert.Equal("QG-FP-1A2B3C4D", first.GetProperty("fingerprint").GetString());
        Assert.Equal(51, first.GetProperty("occurrences").GetInt32());
        Assert.Equal(2, first.GetProperty("firstSequence").GetInt32());
        Assert.Equal(52, first.GetProperty("lastSequence").GetInt32());
        Assert.Contains("Departments", first.GetProperty("sql").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Findings_carry_the_rule_severity_and_expected_against_actual()
    {
        var findings = Parse(_reporter.Render(ReportFixture.FailingResult())).RootElement.GetProperty("findings");

        Assert.Equal(2, findings.GetArrayLength());

        var failure = findings[0];
        Assert.Equal(RuleNames.MaxOccurrencesPerFingerprint, failure.GetProperty("rule").GetString());
        Assert.Equal("Failure", failure.GetProperty("severity").GetString());
        Assert.Equal(5, failure.GetProperty("expected").GetInt64());
        Assert.Equal(51, failure.GetProperty("actual").GetInt64());
        Assert.False(failure.GetProperty("ignored").GetBoolean());
    }

    [Fact]
    public void A_session_wide_finding_emits_a_null_fingerprint_rather_than_omitting_the_field()
    {
        // A consumer reading `finding.fingerprint` should not have to handle "sometimes absent" as well
        // as "sometimes null".
        var json = _reporter.Render(new QueryGuardResult(
            "GET /api/companies",
            ReportFixture.FixedSessionId,
            "companies",
            ReportFixture.FixedInstant,
            TimeSpan.Zero,
            [],
            [],
            [
                new QueryFinding(
                    QueryFindingKind.TotalQueryBudget,
                    QueryGuardSeverity.Failure,
                    "Request executed 27 queries; the budget is 20.",
                    RuleNames.MaxQueries,
                    expected: 20,
                    actual: 27),
            ]));

        var finding = Parse(json).RootElement.GetProperty("findings")[0];

        Assert.Equal(JsonValueKind.Null, finding.GetProperty("fingerprint").ValueKind);
        Assert.Equal(JsonValueKind.Null, finding.GetProperty("ignoreReason").ValueKind);
    }

    [Fact]
    public void An_ignored_finding_is_emitted_with_its_reason_rather_than_dropped()
    {
        // A report that hides ignored findings turns an allowlist into a blind spot.
        var findings = Parse(_reporter.Render(ReportFixture.IgnoredResult())).RootElement.GetProperty("findings");

        var finding = findings[0];
        Assert.True(finding.GetProperty("ignored").GetBoolean());
        Assert.Equal(
            "Bounded provider lookup; at most three report sections.",
            finding.GetProperty("ignoreReason").GetString());
    }

    [Fact]
    public void A_clean_result_still_produces_a_document()
    {
        var document = Parse(_reporter.Render(ReportFixture.CleanResult()));

        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, document.RootElement.GetProperty("findings").GetArrayLength());
    }

    [Fact]
    public void Output_is_byte_identical_across_runs()
    {
        // Without this, a snapshot test is worthless and every CI diff is noise.
        var result = ReportFixture.FailingResult();

        Assert.Equal(_reporter.Render(result), _reporter.Render(result));
    }

    [Fact]
    public void Output_can_be_rendered_without_indentation()
    {
        var compact = new QueryGuardJsonReporter(indented: false).Render(ReportFixture.FailingResult());

        Assert.DoesNotContain('\n', compact);
        Assert.Equal(
            QueryGuardJsonReporter.SchemaVersion,
            Parse(compact).RootElement.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public async Task Writing_to_a_stream_produces_the_same_bytes_without_a_byte_order_mark()
    {
        // A BOM confuses tools that diff report files, and nothing consuming JSON wants one.
        var result = ReportFixture.FailingResult();

        using var buffer = new MemoryStream();
        await _reporter.WriteAsync(result, buffer);

        var bytes = buffer.ToArray();

        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal(_reporter.Render(result), System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Writing_to_a_file_creates_the_directory()
    {
        // The usual destination is an artifacts folder a CI job has not created yet, and failing on
        // that would be a pointless obstacle in the middle of a test run.
        var directory = Path.Combine(Path.GetTempPath(), "queryguard-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "nested", "report" + _reporter.FileExtension);

        try
        {
            await _reporter.WriteAsync(ReportFixture.FailingResult(), path);

            Assert.True(File.Exists(path));
            Assert.Equal(
                QueryGuardJsonReporter.SchemaVersion,
                Parse(await File.ReadAllTextAsync(path)).RootElement.GetProperty("schemaVersion").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Required_arguments_are_validated_when_writing()
    {
        using var buffer = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentNullException>(() => _reporter.WriteAsync(null!, buffer));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _reporter.WriteAsync(ReportFixture.FailingResult(), (Stream)null!));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _reporter.WriteAsync(ReportFixture.FailingResult(), "  "));
    }

    [Fact]
    public void The_file_extension_matches_the_format()
        => Assert.Equal(".json", _reporter.FileExtension);

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);
}
