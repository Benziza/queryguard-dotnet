using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace QueryGuard.Tests;

public class QueryGuardResultTests
{
    [Fact]
    public void A_result_with_no_findings_succeeds()
    {
        var result = Result();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(0, result.WarningCount);
        Assert.Equal(0, result.IgnoredFindingCount);
    }

    [Fact]
    public void Warnings_do_not_fail_a_result()
    {
        // A warning is evidence worth reading. A failure is a verdict. Conflating them would make
        // the default configuration break builds, which is the fastest way to get uninstalled.
        var result = Result(findings:
        [
            Finding(QueryGuardSeverity.Warning),
            Finding(QueryGuardSeverity.Warning),
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.WarningCount);
    }

    [Fact]
    public void A_single_failure_fails_the_result()
    {
        var result = Result(findings: [Finding(QueryGuardSeverity.Warning), Finding(QueryGuardSeverity.Failure)]);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.FailureCount);
        Assert.Equal(1, result.WarningCount);
    }

    [Fact]
    public void An_ignored_failure_does_not_fail_the_result_but_is_still_reported()
    {
        // An allowlist that removed findings would become the place real problems go to die.
        var result = Result(findings: [Finding(QueryGuardSeverity.Failure, isIgnored: true)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(1, result.IgnoredFindingCount);
        Assert.Single(result.Findings);
        Assert.Equal("bounded provider lookup", result.Findings[0].IgnoreReason);
    }

    [Fact]
    public void Informational_findings_count_as_neither_warnings_nor_failures()
    {
        var result = Result(findings: [Finding(QueryGuardSeverity.Information)]);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.WarningCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Single(result.Findings);
    }

    [Fact]
    public void Read_commands_are_counted_separately_from_writes()
    {
        var result = Result(records:
        [
            TestData.Record(sequence: 1, kind: QueryCommandKind.Reader),
            TestData.Record(sequence: 2, kind: QueryCommandKind.Scalar),
            TestData.Record(sequence: 3, kind: QueryCommandKind.NonQuery),
        ]);

        Assert.Equal(3, result.TotalCommandCount);
        Assert.Equal(2, result.ReadCommandCount);
    }

    [Fact]
    public void Total_database_duration_sums_every_captured_command()
    {
        var result = Result(records:
        [
            TestData.Record(sequence: 1, durationMs: 10),
            TestData.Record(sequence: 2, durationMs: 4.5),
        ]);

        Assert.Equal(TimeSpan.FromMilliseconds(14.5), result.TotalDatabaseDuration);
    }

    [Fact]
    public void The_top_repeated_group_is_the_first_group()
    {
        // Groups arrive ordered by descending occurrence count, so the first one is what a failure
        // message should lead with.
        var busiest = Group(TestData.FingerprintFor(1), occurrences: 51);
        var quieter = Group(TestData.FingerprintFor(2), occurrences: 3);

        var result = Result(groups: [busiest, quieter]);

        Assert.Same(busiest, result.TopRepeatedGroup);
    }

    [Fact]
    public void The_top_repeated_group_is_null_when_nothing_was_captured()
        => Assert.Null(Result().TopRepeatedGroup);

    [Fact]
    public void Required_arguments_are_validated()
    {
        var empty = Array.Empty<QueryRecord>();
        var groups = Array.Empty<QueryFingerprintGroup>();
        var findings = Array.Empty<QueryFinding>();

        Assert.Throws<ArgumentNullException>(() => new QueryGuardResult(
            null!, Guid.NewGuid(), "policy", TestData.FixedInstant, TimeSpan.Zero, empty, groups, findings));

        Assert.Throws<ArgumentNullException>(() => new QueryGuardResult(
            "session", Guid.NewGuid(), null!, TestData.FixedInstant, TimeSpan.Zero, empty, groups, findings));

        Assert.Throws<ArgumentNullException>(() => new QueryGuardResult(
            "session", Guid.NewGuid(), "policy", TestData.FixedInstant, TimeSpan.Zero, null!, groups, findings));
    }

    [Fact]
    public void Collections_exposed_by_a_result_are_read_only()
    {
        // A caller must not be able to edit the finding set it was handed.
        foreach (var property in typeof(QueryGuardResult).GetProperties())
        {
            var type = property.PropertyType;
            if (!type.IsGenericType)
            {
                continue;
            }

            var definition = type.GetGenericTypeDefinition();
            Assert.True(
                definition == typeof(IReadOnlyList<>),
                $"{property.Name} exposes {definition.Name}; public collections must be read-only.");
        }
    }

    [Fact]
    public void The_string_representation_summarizes_the_numbers_a_reader_needs_first()
    {
        var result = Result(
            records: [TestData.Record(sequence: 1), TestData.Record(sequence: 2)],
            groups: [Group(TestData.FingerprintFor(1), occurrences: 2)],
            findings: [Finding(QueryGuardSeverity.Failure)]);

        var text = result.ToString();

        Assert.Contains("GET /api/companies", text, StringComparison.Ordinal);
        Assert.Contains("2 read queries", text, StringComparison.Ordinal);
        Assert.Contains("1 failures", text, StringComparison.Ordinal);
    }

    private static QueryGuardResult Result(
        IReadOnlyList<QueryRecord>? records = null,
        IReadOnlyList<QueryFingerprintGroup>? groups = null,
        IReadOnlyList<QueryFinding>? findings = null)
        => new(
            sessionName: "GET /api/companies",
            sessionId: Guid.NewGuid(),
            policyName: "companies",
            startedAt: TestData.FixedInstant,
            elapsed: TimeSpan.FromMilliseconds(120),
            records: records ?? Array.Empty<QueryRecord>(),
            groups: groups ?? Array.Empty<QueryFingerprintGroup>(),
            findings: findings ?? Array.Empty<QueryFinding>());

    private static QueryFinding Finding(QueryGuardSeverity severity, bool isIgnored = false)
        => new(
            kind: QueryFindingKind.RepeatedQueryCandidate,
            severity: severity,
            message: "Potential N+1 pattern: fingerprint QG-FP-1A2B3C4D executed 12 times.",
            ruleName: "repeated-query",
            fingerprint: TestData.Fingerprint(),
            expected: 3,
            actual: 12,
            isIgnored: isIgnored,
            ignoreReason: isIgnored ? "bounded provider lookup" : null);

    private static QueryFingerprintGroup Group(QueryFingerprint fingerprint, int occurrences)
        => new(
            fingerprint: fingerprint,
            occurrences: occurrences,
            totalDuration: TimeSpan.FromMilliseconds(occurrences * 1.5),
            firstSequence: 1,
            lastSequence: occurrences,
            kind: QueryCommandKind.Reader,
            samples: [TestData.Record(fingerprint: fingerprint)]);
}
