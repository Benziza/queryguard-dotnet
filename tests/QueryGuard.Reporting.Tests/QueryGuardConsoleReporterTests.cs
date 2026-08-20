using System;
using System.Linq;
using Xunit;

namespace QueryGuard.Reporting.Tests;

public class QueryGuardConsoleReporterTests
{
    private readonly QueryGuardConsoleReporter _reporter = new();

    [Fact]
    public void A_result_is_required()
        => Assert.Throws<ArgumentNullException>(() => _reporter.Render(null!));

    [Fact]
    public void The_first_line_is_the_verdict()
    {
        // A reader scanning a CI log needs to know in one line whether to keep reading.
        var failing = FirstLine(_reporter.Render(ReportFixture.FailingResult()));
        var clean = FirstLine(_reporter.Render(ReportFixture.CleanResult()));

        Assert.StartsWith("QueryGuard FAILED: GET /api/companies", failing, StringComparison.Ordinal);
        Assert.StartsWith("QueryGuard passed: GET /api/companies", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void The_header_reports_counts_and_database_time()
    {
        var rendered = _reporter.Render(ReportFixture.FailingResult());

        Assert.Contains("52 read queries in 2 distinct queries", rendered, StringComparison.Ordinal);
        Assert.Contains("ms database time", rendered, StringComparison.Ordinal);
        Assert.Contains("1 failures, 1 warnings, 0 ignored", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Queries_are_listed_most_repeated_first()
    {
        var rendered = _reporter.Render(ReportFixture.FailingResult());

        var busy = rendered.IndexOf("QG-FP-1A2B3C4D", StringComparison.Ordinal);
        var quiet = rendered.IndexOf("QG-FP-9E8D7C6B", StringComparison.Ordinal);

        Assert.True(busy >= 0 && quiet >= 0);
        Assert.True(busy < quiet, "The most repeated query should be listed first.");
    }

    [Fact]
    public void Findings_are_labelled_by_outcome()
    {
        var failing = _reporter.Render(ReportFixture.FailingResult());
        var ignored = _reporter.Render(ReportFixture.IgnoredResult());

        Assert.Contains("[FAIL]", failing, StringComparison.Ordinal);
        Assert.Contains("[WARN]", failing, StringComparison.Ordinal);
        Assert.Contains("[IGNORED]", ignored, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ignored_finding_shows_its_reason()
    {
        var rendered = _reporter.Render(ReportFixture.IgnoredResult());

        Assert.Contains("reason: Bounded provider lookup", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void The_caveat_about_what_repeated_sql_proves_reaches_the_reader()
    {
        // Someone reading a CI log usually has nothing else in front of them, so the limitation travels
        // with the finding rather than living only in the documentation.
        var rendered = _reporter.Render(ReportFixture.FailingResult());

        Assert.Contains("strong evidence, not proof", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_output_is_truncated_and_says_so()
    {
        // A report that quietly shows ten of forty reads as "there were ten".
        var rendered = _reporter.Render(ReportFixture.ManyFindingsResult(25));

        Assert.Contains("more distinct queries.", rendered, StringComparison.Ordinal);
        Assert.Contains("more finding(s).", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void A_clean_result_lists_its_queries_and_no_findings()
    {
        var rendered = _reporter.Render(ReportFixture.CleanResult());

        Assert.Contains("Queries by frequency:", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Findings:", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_is_identical_across_runs_and_uses_unix_line_endings()
    {
        var result = ReportFixture.FailingResult();
        var rendered = _reporter.Render(result);

        Assert.Equal(rendered, _reporter.Render(result));
        Assert.DoesNotContain('\r', rendered);
    }

    [Fact]
    public void The_file_extension_matches_the_format()
        => Assert.Equal(".txt", _reporter.FileExtension);

    private static string FirstLine(string value) => value.Split('\n').First();
}
