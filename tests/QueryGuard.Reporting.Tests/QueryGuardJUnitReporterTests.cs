using System;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace QueryGuard.Reporting.Tests;

public class QueryGuardJUnitReporterTests
{
    private readonly QueryGuardJUnitReporter _reporter = new();

    [Fact]
    public void A_result_is_required()
        => Assert.Throws<ArgumentNullException>(() => _reporter.Render(null!));

    [Fact]
    public void The_output_is_well_formed_xml()
    {
        // A malformed report is worse than no report: most CI systems fail the step rather than
        // ignoring it, so a broken reporter would look like a broken build.
        var document = XDocument.Parse(_reporter.Render(ReportFixture.FailingResult()));

        Assert.Equal("testsuites", document.Root!.Name.LocalName);
    }

    [Fact]
    public void The_suite_is_named_after_the_scope_and_counts_its_cases()
    {
        var suite = Suite(ReportFixture.FailingResult());

        Assert.Equal("GET /api/companies", suite.Attribute("name")!.Value);
        Assert.Equal("2", suite.Attribute("tests")!.Value);
        Assert.Equal("1", suite.Attribute("failures")!.Value);
        Assert.Equal("0", suite.Attribute("skipped")!.Value);
        Assert.Equal("0", suite.Attribute("errors")!.Value);
    }

    [Fact]
    public void A_failure_becomes_a_failing_case_carrying_its_evidence()
    {
        var cases = Cases(ReportFixture.FailingResult());

        var failing = Assert.Single(cases, element => element.Element("failure") is not null);
        var failure = failing.Element("failure")!;

        Assert.Contains("QG-FP-1A2B3C4D", failing.Attribute("name")!.Value, StringComparison.Ordinal);
        Assert.Equal(RuleNames.MaxOccurrencesPerFingerprint, failure.Attribute("type")!.Value);
        Assert.Contains("the budget is 5", failure.Attribute("message")!.Value, StringComparison.Ordinal);
        Assert.Contains("Occurrences: 51 (budget: 5)", failure.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_warning_becomes_a_passing_case_with_its_evidence_attached()
    {
        // Failing a CI suite on a repeated-query candidate by default would break the first build
        // QueryGuard runs in, and a tool that does that gets switched off rather than tuned.
        var cases = Cases(ReportFixture.FailingResult());

        var warning = Assert.Single(
            cases,
            element => element.Attribute("name")!.Value.StartsWith(RuleNames.RepeatedQuery, StringComparison.Ordinal));

        Assert.Null(warning.Element("failure"));
        Assert.Null(warning.Element("skipped"));
        Assert.Contains("Potential N+1 pattern", warning.Element("system-out")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ignored_finding_becomes_a_skipped_case_carrying_its_reason()
    {
        var suite = Suite(ReportFixture.IgnoredResult());
        var skipped = Assert.Single(suite.Elements("testcase"), element => element.Element("skipped") is not null);

        Assert.Equal("1", suite.Attribute("skipped")!.Value);
        Assert.Equal("0", suite.Attribute("failures")!.Value);
        Assert.Contains(
            "Bounded provider lookup",
            skipped.Element("skipped")!.Attribute("message")!.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_clean_result_still_produces_one_passing_case()
    {
        // An empty suite renders as "no tests" in most CI viewers, which reads as "QueryGuard did not
        // run" — the opposite of the truth.
        var cases = Cases(ReportFixture.CleanResult());

        var single = Assert.Single(cases);

        Assert.Null(single.Element("failure"));
        Assert.Contains("no findings", single.Element("system-out")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_suite_carries_the_policy_and_query_counts_as_properties()
    {
        // So a CI viewer shows the context without anyone opening the JSON report.
        var properties = Suite(ReportFixture.FailingResult())
            .Element("properties")!
            .Elements("property")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Attribute("value")!.Value,
                StringComparer.Ordinal);

        Assert.Equal("companies", properties["queryguard.policy"]);
        Assert.Equal("52", properties["queryguard.readCommands"]);
        Assert.Equal("2", properties["queryguard.distinctQueries"]);
    }

    [Fact]
    public void Output_is_byte_identical_across_runs_and_platforms()
    {
        // Line endings are forced to \n so the same result renders identically on Windows and Linux.
        // Otherwise a snapshot test can only pass on one of them.
        var result = ReportFixture.FailingResult();
        var rendered = _reporter.Render(result);

        Assert.Equal(rendered, _reporter.Render(result));
        Assert.DoesNotContain('\r', rendered);
    }

    [Fact]
    public void Sql_and_messages_survive_a_round_trip_through_xml()
    {
        // SQL is full of characters XML cares about — quotes in identifiers, and `<` or `&` in a
        // predicate. Asserting on a specific escape sequence would be asserting on XmlWriter's
        // choices; what matters is that the text comes back out exactly as it went in.
        const string AwkwardSql = "SELECT * FROM \"T\" WHERE \"a\" < ? AND \"b\" & ? > ? AND \"c\" = 'x'";

        var fingerprint = new QueryFingerprint(QueryFingerprint.IdPrefix + "AWKWARD1", AwkwardSql);

        var result = new QueryGuardResult(
            "GET /api/companies",
            ReportFixture.FixedSessionId,
            "companies",
            ReportFixture.FixedInstant,
            TimeSpan.Zero,
            [],
            [],
            [
                new QueryFinding(
                    QueryFindingKind.FingerprintOccurrenceBudget,
                    QueryGuardSeverity.Failure,
                    "Budget exceeded for <awkward> & \"quoted\" query.",
                    RuleNames.MaxOccurrencesPerFingerprint,
                    fingerprint,
                    expected: 1,
                    actual: 9,
                    evidence: [$"SQL: {AwkwardSql}"]),
            ]);

        var failure = XDocument.Parse(_reporter.Render(result))
            .Descendants("failure")
            .Single();

        Assert.Equal("Budget exceeded for <awkward> & \"quoted\" query.", failure.Attribute("message")!.Value);
        Assert.Contains(AwkwardSql, failure.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_extension_matches_the_format()
        => Assert.Equal(".xml", _reporter.FileExtension);

    private static XElement Suite(QueryGuardResult result)
        => XDocument.Parse(new QueryGuardJUnitReporter().Render(result)).Root!.Element("testsuite")!;

    private static XElement[] Cases(QueryGuardResult result) => [.. Suite(result).Elements("testcase")];
}
