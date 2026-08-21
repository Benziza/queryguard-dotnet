using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Xunit;

namespace QueryGuard.Testing.Tests;

/// <summary>
/// The failure message is the product here, so it is asserted on directly.
/// </summary>
/// <remarks>
/// Because QueryGuard takes no test framework dependency, there is no native formatter to lean on and
/// the exception message has to carry the whole evidence trail. A failure a developer cannot act on
/// without opening documentation is a bug, so these tests read the message.
/// </remarks>
public class QueryGuardAssertTests
{
    [Fact]
    public void A_result_is_required()
    {
        Assert.Throws<ArgumentNullException>(() => QueryGuardAssert.Passes(null!));
        Assert.Throws<ArgumentNullException>(() => QueryGuardAssert.HasNoWarnings(null!));
        Assert.Throws<ArgumentNullException>(() => QueryGuardAssert.ExecutedQueryCount(1, null!));
        Assert.Throws<ArgumentNullException>(() => QueryGuardAssert.NoQueryRepeatedMoreThan(1, null!));
        Assert.Throws<ArgumentNullException>(() => QueryGuardAssert.Describe(null!));
    }

    [Fact]
    public void A_passing_result_does_not_throw()
    {
        var result = Analyze(QueryGuardPolicy.Create("p").WithMaxQueries(10), ("A", 2));

        QueryGuardAssert.Passes(result);
    }

    [Fact]
    public void A_warning_does_not_fail_Passes()
    {
        // Making a candidate warning fail by default would break the first build QueryGuard is
        // installed in.
        var result = Analyze(QueryGuardPolicy.Create("p"), ("A", 12));

        Assert.Equal(1, result.WarningCount);
        QueryGuardAssert.Passes(result);
    }

    [Fact]
    public void A_warning_does_fail_HasNoWarnings()
    {
        var result = Analyze(QueryGuardPolicy.Create("p"), ("A", 12));

        Assert.Throws<QueryGuardBudgetExceededException>(() => QueryGuardAssert.HasNoWarnings(result));
    }

    [Fact]
    public void A_failure_throws_and_the_exception_carries_the_result()
    {
        // So a test can inspect findings programmatically instead of parsing the message.
        var result = Analyze(QueryGuardPolicy.Create("companies").WithMaxQueries(2), ("A", 12));

        var exception = Assert.Throws<QueryGuardBudgetExceededException>(() => QueryGuardAssert.Passes(result));

        Assert.Same(result, exception.Result);
    }

    [Fact]
    public void The_failure_message_names_the_policy_and_the_scope()
    {
        var result = Analyze(QueryGuardPolicy.Create("companies").WithMaxQueries(2), ("A", 12));

        var message = Assert.Throws<QueryGuardBudgetExceededException>(() => QueryGuardAssert.Passes(result)).Message;

        Assert.Contains("companies", message, StringComparison.Ordinal);
        Assert.Contains("GET /api/companies", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_failure_message_reports_the_numbers_that_matter()
    {
        var result = Analyze(QueryGuardPolicy.Create("companies").WithMaxQueries(2), ("A", 12));

        var message = Assert.Throws<QueryGuardBudgetExceededException>(() => QueryGuardAssert.Passes(result)).Message;

        Assert.Contains("Read queries:        12", message, StringComparison.Ordinal);
        Assert.Contains("Distinct queries:    1", message, StringComparison.Ordinal);
        Assert.Contains("Database time:", message, StringComparison.Ordinal);
        Assert.Contains("Failures / warnings: 1 / 1", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_failure_message_leads_with_the_most_repeated_query_and_its_sql()
    {
        // Of everything QueryGuard saw, that group is the most likely to be the actual problem.
        var result = Analyze(QueryGuardPolicy.Create("p").WithMaxQueries(2), ("busy", 12), ("quiet", 1));

        var message = Assert.Throws<QueryGuardBudgetExceededException>(() => QueryGuardAssert.Passes(result)).Message;

        Assert.Contains("Most repeated query: QG-FP-busy x12", message, StringComparison.Ordinal);
        Assert.Contains("SELECT * FROM \"busy\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_failure_message_points_at_the_false_positive_guide_rather_than_at_disabling_the_tool()
    {
        var result = Analyze(QueryGuardPolicy.Create("p").WithMaxQueries(2), ("A", 12));

        var message = Assert.Throws<QueryGuardBudgetExceededException>(() => QueryGuardAssert.Passes(result)).Message;

        Assert.Contains("do not disable QueryGuard", message, StringComparison.Ordinal);
        Assert.Contains("allowlist entry with a reason", message, StringComparison.Ordinal);
        Assert.Contains("false-positives", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_failure_message_shows_ignored_findings_with_their_reason()
    {
        var policy = QueryGuardPolicy.Create("p")
            .WithMaxQueries(2)
            .AllowFingerprint(QueryFingerprint.IdPrefix + "A", "bounded provider lookup");

        var result = Analyze(policy, ("A", 12));

        var message = Assert.Throws<QueryGuardBudgetExceededException>(() => QueryGuardAssert.Passes(result)).Message;

        Assert.Contains("Ignored findings:", message, StringComparison.Ordinal);
        Assert.Contains("[ignored]", message, StringComparison.Ordinal);
        Assert.Contains("reason: bounded provider lookup", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_failure_message_is_bounded_and_says_when_it_truncates()
    {
        // A pathological session can produce dozens of findings. Flooding a CI log would bury the one
        // that matters, and a report that quietly shows five of forty reads as "there were five".
        var policy = QueryGuardPolicy.Create("p").WithMaxOccurrencesPerFingerprint(1);
        var commands = Enumerable.Range(0, 20)
            .Select(index => (string.Create(CultureInfo.InvariantCulture, $"q{index:00}"), 4))
            .ToArray();

        var result = Analyze(policy, commands);
        var message = Assert.Throws<QueryGuardBudgetExceededException>(() => QueryGuardAssert.Passes(result)).Message;

        Assert.Contains("more finding(s) not shown", message, StringComparison.Ordinal);

        // Bounded, so the message stays readable in a terminal.
        Assert.True(message.Length < 6_000, $"The failure message was {message.Length} characters long.");
    }

    [Fact]
    public void An_exact_query_count_catches_both_more_and_fewer_queries()
    {
        // A refactor that removes a query is usually good news and occasionally means a feature quietly
        // stopped loading something.
        var tooMany = Analyze(QueryGuardPolicy.Create("p"), ("A", 3));
        var tooFew = Analyze(QueryGuardPolicy.Create("p"), ("A", 1));

        QueryGuardAssert.ExecutedQueryCount(3, tooMany);
        Assert.Throws<QueryGuardBudgetExceededException>(() => QueryGuardAssert.ExecutedQueryCount(2, tooMany));
        Assert.Throws<QueryGuardBudgetExceededException>(() => QueryGuardAssert.ExecutedQueryCount(2, tooFew));
    }

    [Fact]
    public void An_exact_query_count_failure_says_what_it_expected_and_saw()
    {
        var result = Analyze(QueryGuardPolicy.Create("p"), ("A", 7));

        var message = Assert.Throws<QueryGuardBudgetExceededException>(
            () => QueryGuardAssert.ExecutedQueryCount(3, result)).Message;

        Assert.Contains("expected exactly 3 counted queries", message, StringComparison.Ordinal);
        Assert.Contains("but 7 ran", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repetition_limit_identifies_the_offending_fingerprint()
    {
        var result = Analyze(QueryGuardPolicy.Create("p"), ("busy", 9), ("quiet", 1));

        QueryGuardAssert.NoQueryRepeatedMoreThan(9, result);

        var message = Assert.Throws<QueryGuardBudgetExceededException>(
            () => QueryGuardAssert.NoQueryRepeatedMoreThan(3, result)).Message;

        Assert.Contains("QG-FP-busy ran 9 times", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repetition_limit_of_zero_is_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => QueryGuardAssert.NoQueryRepeatedMoreThan(0, Analyze(QueryGuardPolicy.Create("p"), ("A", 1))));

    [Fact]
    public void An_empty_result_satisfies_every_assertion()
    {
        var result = Analyze(QueryGuardPolicy.Create("p"));

        QueryGuardAssert.Passes(result);
        QueryGuardAssert.HasNoWarnings(result);
        QueryGuardAssert.ExecutedQueryCount(0, result);
        QueryGuardAssert.NoQueryRepeatedMoreThan(1, result);
    }

    [Fact]
    public void Describe_renders_a_passing_result_too()
    {
        // Useful while tuning a budget: print what actually happened instead of guessing at a number.
        var description = QueryGuardAssert.Describe(Analyze(QueryGuardPolicy.Create("p"), ("A", 4)));

        Assert.Contains("Read queries:        4", description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_testing_package_references_no_test_framework()
    {
        // The reason this package can be installed by an NUnit or MSTest project without dragging xUnit
        // in. Asserted structurally, because it is the kind of dependency that gets added by accident.
        var referenced = typeof(QueryGuardAssert).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            referenced,
            assembly => assembly.Name is not null
                && (assembly.Name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
                    || assembly.Name.StartsWith("nunit", StringComparison.OrdinalIgnoreCase)
                    || assembly.Name.Contains("VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase)
                    || assembly.Name.Contains("TUnit", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_exception_type_has_the_constructors_a_plain_exception_should()
    {
        // It is deliberately an ordinary exception so that every framework renders it. That means it
        // should also behave like one.
        Assert.NotNull(new QueryGuardBudgetExceededException().Message);
        Assert.Equal("boom", new QueryGuardBudgetExceededException("boom").Message);

        var inner = new InvalidOperationException("inner");
        Assert.Same(inner, new QueryGuardBudgetExceededException("boom", inner).InnerException);
    }

    private static QueryGuardResult Analyze(
        QueryGuardPolicy policy,
        params (string Fingerprint, int Times)[] commands)
    {
        var session = new QueryGuardSession("GET /api/companies", policy);

        foreach (var (suffix, times) in commands)
        {
            var fingerprint = new QueryFingerprint(
                QueryFingerprint.IdPrefix + suffix,
                string.Create(CultureInfo.InvariantCulture, $"SELECT * FROM \"{suffix}\" WHERE \"Id\" = ?"));

            for (var i = 0; i < times; i++)
            {
                session.Record(QueryCommandKind.Reader, fingerprint, TimeSpan.FromMilliseconds(1));
            }
        }

        return new QueryGuardAnalyzer().Analyze(session.Complete());
    }
}
