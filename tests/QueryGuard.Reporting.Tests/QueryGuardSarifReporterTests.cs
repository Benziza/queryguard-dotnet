using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace QueryGuard.Reporting.Tests;

/// <summary>
/// The SARIF reporter.
/// </summary>
/// <remarks>
/// The failure mode this format has that the others do not: a document can be valid SARIF, upload
/// without complaint, and still show the wrong thing: an alert with no annotation, an annotation on the
/// wrong file, or a fresh alert on every run because the identity moved. So the assertions here are about
/// the fields GitHub actually reads, not only about the document parsing.
/// </remarks>
public class QueryGuardSarifReporterTests
{
    private const string Root = "C:/repo";

    private const string Fallback = "tests/Measured.cs";

    private static readonly string Trace = string.Join(
        '\n',
        "at Sample.Api.Endpoints.<List>b__0(AppDbContext db)",
        "at Sample.Api.Endpoints.List(AppDbContext db) in C:\\repo\\src\\Endpoints.cs:line 89");

    [Fact]
    public void The_document_declares_sarif_2_1_0_with_one_run()
    {
        using var document = JsonDocument.Parse(new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult()));
        var root = document.RootElement;

        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        Assert.True(root.TryGetProperty("$schema", out _));
        Assert.Single(root.GetProperty("runs").EnumerateArray());
    }

    [Fact]
    public void The_extension_is_sarif()
    {
        // GitHub's upload action matches on .sarif; ".json" would be silently ignored by the glob most
        // workflows use.
        Assert.Equal(".sarif", new QueryGuardSarifReporter().FileExtension);
    }

    [Fact]
    public void Only_the_rules_this_run_produced_are_declared()
    {
        // Declaring all seven every time would leave the Security tab listing rules that never fired,
        // which reads as coverage rather than silence.
        var rules = Rules(new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult()));

        Assert.Equal(
            [RuleNames.MaxOccurrencesPerFingerprint, RuleNames.RepeatedQuery],
            rules.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_clean_result_produces_no_results_and_no_rules()
    {
        var sarif = new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.CleanResult());

        Assert.Empty(Results(sarif));
        Assert.Empty(Rules(sarif));
    }

    [Fact]
    public void A_repeated_query_candidate_is_a_warning_and_never_an_error()
    {
        // The rule that keeps the check tunable rather than switched off. A candidate is evidence, and
        // a red X on evidence is how a tool gets removed from a pipeline.
        var results = Results(new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult()));

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.Equal("warning", result.GetProperty("level").GetString()));
    }

    [Fact]
    public void A_budget_failure_is_still_only_a_warning()
    {
        // The fixture's per-fingerprint finding carries Severity.Failure, which fails the build through
        // the assertion. That is the assertion's job; repeating it here as an error would put a severity
        // in the Security tab that a reader cannot tune.
        var result = Results(new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult()))
            .Single(r => r.GetProperty("ruleId").GetString() == RuleNames.MaxOccurrencesPerFingerprint);

        Assert.Equal("warning", result.GetProperty("level").GetString());
    }

    [Fact]
    public void The_message_names_the_scope()
    {
        // An annotation is read on a diff with no report around it: without the scope, "executed 51
        // times" does not say during what.
        var result = Results(new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult()))
            .Single(r => r.GetProperty("ruleId").GetString() == RuleNames.MaxOccurrencesPerFingerprint);

        var message = result.GetProperty("message").GetProperty("text").GetString();

        Assert.Contains("GET /api/companies", message!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_scope_is_not_appended_when_the_message_already_names_it()
    {
        // The repeated-query message already reads "Potential N+1 pattern in GET /api/companies: ...".
        // Appending the scope again produced "... (scope: GET /api/companies)" on the end, which reads
        // like a bug because it is one.
        var result = Results(new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult()))
            .Single(r => r.GetProperty("ruleId").GetString() == RuleNames.RepeatedQuery);

        var message = result.GetProperty("message").GetProperty("text").GetString();

        Assert.DoesNotContain("(scope:", message!, StringComparison.Ordinal);
        Assert.Contains("GET /api/companies", message!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ignored_finding_is_suppressed_rather_than_dropped()
    {
        // The project's rule is that an allowlisted finding stays visible with its reason. Omitting it
        // would make the report claim the repetition is not there.
        var result = Assert.Single(Results(new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.IgnoredResult())));

        var suppression = Assert.Single(result.GetProperty("suppressions").EnumerateArray());
        Assert.Equal("accepted", suppression.GetProperty("status").GetString());
        Assert.Contains(
            "Bounded provider lookup",
            suppression.GetProperty("justification").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_finding_that_was_not_ignored_carries_no_suppression()
    {
        var result = Results(new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult())).First();

        Assert.False(result.TryGetProperty("suppressions", out _));
    }

    [Fact]
    public void An_origin_becomes_a_repository_relative_location()
    {
        // The only form GitHub can match against a diff. An absolute path uploads fine and annotates
        // nothing, which looks like the reporter working.
        var location = SingleLocation(Render(withTrace: true, root: Root));

        Assert.Equal(
            "src/Endpoints.cs",
            location.GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal(89, location.GetProperty("region").GetProperty("startLine").GetInt32());
    }

    [Fact]
    public void A_path_outside_the_repository_root_is_left_absolute_rather_than_mangled()
    {
        // Trimming a prefix that is not there would produce a plausible relative path pointing at the
        // wrong file, and an annotation on innocent code is worse than no annotation.
        var location = SingleLocation(Render(withTrace: true, root: "D:/elsewhere"));

        Assert.Equal(
            "C:/repo/src/Endpoints.cs",
            location.GetProperty("artifactLocation").GetProperty("uri").GetString());
    }

    [Fact]
    public void Every_emitted_result_has_a_location_because_github_rejects_the_file_otherwise()
    {
        // Learned from a rejected upload, not from the schema. The schema permits a result with no
        // locations; GitHub answers "locationFromSarifResult: expected at least one location" and
        // rejects the WHOLE file, so one location-less result loses every other finding with it.
        foreach (var sarif in new[]
                 {
                     new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult()),
                     new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult()),
                     Render(withTrace: false, root: Root, fallback: Fallback),
                     Render(withTrace: true, root: Root),
                 })
        {
            Assert.All(
                Results(sarif),
                result => Assert.NotEmpty(result.GetProperty("locations").EnumerateArray()));
        }
    }

    [Theory]
    [InlineData("/_/samples/Api/Program.cs", "samples/Api/Program.cs")]
    [InlineData("/_1/samples/Api/Program.cs", "samples/Api/Program.cs")]
    [InlineData("/_12/src/Thing.cs", "src/Thing.cs")]
    public void A_deterministic_build_path_is_mapped_to_a_repository_relative_uri(string recorded, string expected)
    {
        // Found by uploading and reading the alert, not by reading the schema. Setting
        // ContinuousIntegrationBuild - which this repository does, and which every SourceLink-using
        // library does in CI - makes the compiler embed "/_/" in place of the source root. The path is
        // already repository-relative but matches no repository root, so it used to pass through and
        // GitHub filed the alert under a path it could not map to the diff. The upload succeeded and
        // every test passed; the annotation simply never appeared.
        var location = SingleLocation(RenderWithPath(recorded));

        Assert.Equal(expected, location.GetProperty("artifactLocation").GetProperty("uri").GetString());
    }

    [Theory]
    [InlineData("/_x/src/Thing.cs")]
    [InlineData("/_/")]
    [InlineData("/_")]
    public void Something_that_only_looks_like_a_deterministic_root_is_left_alone(string recorded)
    {
        // The prefix is "/_" plus optional digits plus a slash plus something. Trimming anything else
        // would corrupt a real path that happened to start similarly.
        var location = SingleLocation(RenderWithPath(recorded));

        Assert.Equal(recorded, location.GetProperty("artifactLocation").GetProperty("uri").GetString());
    }

    [Fact]
    public void A_finding_with_no_origin_lands_on_the_fallback_path_without_a_line()
    {
        // No region, because there is no line to claim. A guessed line number annotates innocent code,
        // which is the one outcome worse than no annotation.
        var location = SingleLocation(Render(withTrace: false, root: Root, fallback: Fallback));

        Assert.Equal(Fallback, location.GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.False(location.TryGetProperty("region", out _));
    }

    [Fact]
    public void Without_a_fallback_a_finding_with_no_origin_is_omitted_and_the_count_is_stated()
    {
        // It cannot be emitted and it must not be silent. There is nowhere in a SARIF result to say so,
        // so it goes on the run.
        var sarif = Render(withTrace: false, root: Root);

        Assert.Empty(Results(sarif));

        using var document = JsonDocument.Parse(sarif);
        var properties = document.RootElement.GetProperty("runs")[0].GetProperty("properties");

        Assert.Equal(1, properties.GetProperty("findingsWithoutLocation").GetInt32());
        Assert.Contains(
            "fallbackPath",
            properties.GetProperty("findingsWithoutLocationNote").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_omitted_when_every_finding_has_an_origin()
    {
        // The counter must not appear when it would read as "some findings were dropped".
        using var document = JsonDocument.Parse(Render(withTrace: true, root: Root));

        Assert.False(document.RootElement.GetProperty("runs")[0].TryGetProperty("properties", out _));
    }

    [Fact]
    public void The_fingerprint_is_the_stable_identity_across_runs()
    {
        // Without a partial fingerprint, GitHub derives identity partly from location: moving the
        // offending call down one line would close one alert and open another.
        var result = Results(new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult())).First();

        var fingerprints = result.GetProperty("partialFingerprints");
        Assert.StartsWith(
            QueryFingerprint.IdPrefix,
            fingerprints.GetProperty("queryGuardFingerprint/v1").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Two_renders_of_the_same_result_are_byte_identical()
    {
        // What makes every other snapshot assertion in this project meaningful, and what stops a CI
        // diff of committed SARIF from being pure noise.
        var reporter = new QueryGuardSarifReporter(Root);
        var result = ReportFixture.FailingResult();

        Assert.Equal(reporter.Render(result), reporter.Render(result));
    }

    [Fact]
    public void Rule_order_does_not_depend_on_finding_order()
    {
        // Rules are emitted sorted, so a change to the order findings are produced in cannot churn the
        // document.
        var rules = Rules(new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult())).ToList();

        Assert.Equal(rules.Order(StringComparer.Ordinal), rules);
    }

    [Fact]
    public void The_tool_version_is_a_semantic_version_without_the_commit()
    {
        // SARIF wants a semantic version. SourceLink appends "+<commit>", which is valid build metadata
        // and noise in a tool banner.
        using var document = JsonDocument.Parse(new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult()));

        var version = document.RootElement
            .GetProperty("runs")[0].GetProperty("tool").GetProperty("driver")
            .GetProperty("semanticVersion").GetString();

        Assert.DoesNotContain("+", version!, StringComparison.Ordinal);
        Assert.Matches(@"^\d+\.\d+\.\d+", version!);
    }

    [Fact]
    public void Nothing_beyond_the_result_reaches_the_document()
    {
        // Redaction happened before this reporter ran. The guard is that the reporter renders the SQL it
        // was handed and reaches for nothing else, so a value that was never captured cannot appear.
        var sarif = new QueryGuardSarifReporter(Root, Fallback).Render(ReportFixture.FailingResult());

        Assert.DoesNotContain("Password", sarif, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source", sarif, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Null_is_rejected_rather_than_producing_an_empty_document()
    {
        Assert.Throws<ArgumentNullException>(() => new QueryGuardSarifReporter().Render(null!));
    }

    private static string Render(bool withTrace, string? root, string? fallback = null)
    {
        var fingerprint = new QueryFingerprint(QueryFingerprint.IdPrefix + "1A2B3C4D", "SELECT 1");

        var result = new QueryGuardResult(
            sessionName: "GET /api/companies",
            sessionId: ReportFixture.FixedSessionId,
            policyName: "companies",
            startedAt: ReportFixture.FixedInstant,
            elapsed: TimeSpan.FromMilliseconds(120),
            records: [],
            groups: [],
            findings:
            [
                new QueryFinding(
                    QueryFindingKind.RepeatedQueryCandidate,
                    QueryGuardSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"Fingerprint {fingerprint.Id} executed 51 times."),
                    RuleNames.RepeatedQuery,
                    fingerprint,
                    stackTrace: withTrace ? Trace : null),
            ]);

        return new QueryGuardSarifReporter(root, fallback).Render(result);
    }

    private static string RenderWithPath(string recordedPath)
    {
        var fingerprint = new QueryFingerprint(QueryFingerprint.IdPrefix + "1A2B3C4D", "SELECT 1");

        var result = new QueryGuardResult(
            sessionName: "GET /api/companies",
            sessionId: ReportFixture.FixedSessionId,
            policyName: "companies",
            startedAt: ReportFixture.FixedInstant,
            elapsed: TimeSpan.FromMilliseconds(120),
            records: [],
            groups: [],
            findings:
            [
                new QueryFinding(
                    QueryFindingKind.RepeatedQueryCandidate,
                    QueryGuardSeverity.Warning,
                    "Fingerprint executed 51 times.",
                    RuleNames.RepeatedQuery,
                    fingerprint,
                    stackTrace: "at T.M() in " + recordedPath + ":line 89"),
            ]);

        return new QueryGuardSarifReporter(Root).Render(result);
    }

    private static JsonElement SingleLocation(string sarif)
    {
        var result = Results(sarif).First();
        return Assert.Single(result.GetProperty("locations").EnumerateArray())
            .GetProperty("physicalLocation");
    }

    private static JsonElement[] Results(string sarif)
    {
        using var document = JsonDocument.Parse(sarif);

        return [.. document.RootElement.GetProperty("runs")[0].GetProperty("results")
            .EnumerateArray()
            .Select(element => element.Clone())];
    }

    private static string[] Rules(string sarif)
    {
        using var document = JsonDocument.Parse(sarif);

        return [.. document.RootElement.GetProperty("runs")[0]
            .GetProperty("tool").GetProperty("driver").GetProperty("rules")
            .EnumerateArray()
            .Select(rule => rule.GetProperty("id").GetString()!)];
    }
}
