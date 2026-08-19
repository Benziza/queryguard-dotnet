using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace QueryGuard.Tests;

/// <summary>
/// The redaction matrix. Every case here is a way QueryGuard could leak data into a report that
/// someone then pastes into a public issue.
/// </summary>
public class QueryGuardRedactorTests
{
    [Fact]
    public void Defaults_capture_the_least_that_still_produces_usable_evidence()
    {
        var options = new QueryGuardCaptureOptions();

        Assert.False(options.CaptureParameterValues);
        Assert.False(options.CaptureFirstStackTrace);
        Assert.True(options.RedactStringLiterals);
        Assert.True(options.RedactNumericLiterals);
        Assert.Equal(3, options.MaxSamplesPerFingerprint);
        Assert.Equal(4096, options.MaxNormalizedSqlLength);
        Assert.NotEmpty(options.StackTraceFrameFilters);
    }

    [Fact]
    public void A_string_literal_is_replaced_but_the_statement_shape_survives()
    {
        var redactor = new QueryGuardRedactor();

        var redacted = redactor.RedactSql(
            "SELECT * FROM \"Users\" WHERE \"Email\" = 'alice@example.com'");

        Assert.DoesNotContain("alice@example.com", redacted, StringComparison.Ordinal);
        Assert.Contains("'?'", redacted, StringComparison.Ordinal);

        // The shape has to remain readable, or the evidence is worthless.
        Assert.Contains("SELECT * FROM \"Users\" WHERE \"Email\" =", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_string_literal_in_a_statement_is_replaced()
    {
        var redactor = new QueryGuardRedactor();

        var redacted = redactor.RedactSql(
            "SELECT 'first', 'second' FROM \"T\" WHERE \"A\" = 'third' OR \"B\" = 'fourth'");

        foreach (var secret in new[] { "first", "second", "third", "fourth" })
        {
            Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_escaped_quote_inside_a_literal_does_not_end_it_early()
    {
        // 'O''Brien' is one literal. Treating the doubled quote as a terminator would leave the
        // second half of the name in the output.
        var redactor = new QueryGuardRedactor();

        var redacted = redactor.RedactSql("SELECT * FROM \"T\" WHERE \"Name\" = 'O''Brien'");

        Assert.DoesNotContain("Brien", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unterminated_literal_does_not_leak_the_rest_of_the_statement()
    {
        var redactor = new QueryGuardRedactor();

        var redacted = redactor.RedactSql("SELECT * FROM \"T\" WHERE \"Name\" = 'unterminated secret");

        Assert.DoesNotContain("unterminated secret", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Numeric_literals_are_replaced()
    {
        var redactor = new QueryGuardRedactor();

        var redacted = redactor.RedactSql("SELECT * FROM \"Accounts\" WHERE \"Number\" = 9900112233445566");

        Assert.DoesNotContain("9900112233445566", redacted, StringComparison.Ordinal);
        Assert.Contains("?", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT * FROM \"T\" WHERE \"X\" = 1.5")]
    [InlineData("SELECT * FROM \"T\" WHERE \"X\" = 1.5e-3")]
    [InlineData("SELECT * FROM \"T\" WHERE \"X\" = 1E10")]
    public void Decimal_and_exponent_notation_is_consumed_as_one_literal(string sql)
    {
        var redactor = new QueryGuardRedactor();

        var redacted = redactor.RedactSql(sql);

        // A partially consumed number would leave a fragment such as "e-3" behind, which is both a
        // leak of the shape and unreadable evidence.
        Assert.EndsWith("= ?", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Digits_inside_an_identifier_or_a_parameter_name_are_left_alone()
    {
        // EF Core aliases look like "t1" and its parameters like "@__city_0". Replacing those digits
        // would corrupt the statement without protecting anything.
        var redactor = new QueryGuardRedactor();

        var redacted = redactor.RedactSql(
            "SELECT \"t1\".\"Id\" FROM \"Departments\" AS \"t1\" WHERE \"t1\".\"City\" = @__city_0");

        Assert.Contains("\"t1\"", redacted, StringComparison.Ordinal);
        Assert.Contains("@__city_0", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"Departments\"")]
    [InlineData("[Departments]")]
    [InlineData("`Departments`")]
    public void Quoted_identifiers_are_preserved_across_provider_syntaxes(string identifier)
    {
        // A table name is structure, not data. Removing it would make the finding unreadable, and
        // every provider quotes identifiers differently.
        var redactor = new QueryGuardRedactor();

        var redacted = redactor.RedactSql($"SELECT * FROM {identifier} WHERE \"Id\" = 42");

        Assert.Contains("Departments", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("42", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Comments_are_preserved_because_stripping_them_belongs_to_the_normalizer()
    {
        // Two places deciding which comments are semantic is one place too many: the QueryGuard
        // ignore tag travels as a comment and must survive this stage.
        var redactor = new QueryGuardRedactor();

        var redacted = redactor.RedactSql(
            "-- QueryGuard:Ignore reason=bounded-lookup\nSELECT * FROM \"T\" WHERE \"Id\" = 7");

        Assert.Contains("QueryGuard:Ignore", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("= 7", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_block_comment_is_preserved_and_does_not_swallow_the_statement()
    {
        var redactor = new QueryGuardRedactor();

        var redacted = redactor.RedactSql("/* tag */ SELECT * FROM \"T\" WHERE \"Id\" = 7");

        Assert.Contains("/* tag */", redacted, StringComparison.Ordinal);
        Assert.Contains("SELECT", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redaction_is_idempotent()
    {
        // Output passes through reporters and can be re-processed. A value must not be able to
        // reappear, and the text must not accumulate placeholders.
        var redactor = new QueryGuardRedactor();
        var once = redactor.RedactSql("SELECT * FROM \"T\" WHERE \"Email\" = 'alice@example.com' AND \"Id\" = 42");

        Assert.Equal(once, redactor.RedactSql(once));
    }

    [Fact]
    public void Literal_redaction_can_be_turned_off_independently()
    {
        var options = new QueryGuardCaptureOptions
        {
            RedactStringLiterals = false,
            RedactNumericLiterals = true,
        };
        var redactor = new QueryGuardRedactor(options);

        var redacted = redactor.RedactSql("SELECT * FROM \"T\" WHERE \"Name\" = 'keep me' AND \"Id\" = 42");

        Assert.Contains("keep me", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("42", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_is_truncated_with_an_explicit_marker()
    {
        // A shortened statement must never be mistakable for the whole one.
        var options = new QueryGuardCaptureOptions { MaxNormalizedSqlLength = 32 };
        var redactor = new QueryGuardRedactor(options);

        var redacted = redactor.RedactSql(new string('A', 200));

        Assert.StartsWith(new string('A', 32), redacted, StringComparison.Ordinal);
        Assert.Contains("truncated by QueryGuard", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_or_empty_sql_becomes_an_empty_string_rather_than_null()
    {
        var redactor = new QueryGuardRedactor();

        Assert.Equal(string.Empty, redactor.RedactSql(null));
        Assert.Equal(string.Empty, redactor.RedactSql(string.Empty));
    }

    [Fact]
    public void Framework_frames_are_filtered_out_of_a_stack_trace()
    {
        var redactor = new QueryGuardRedactor();

        var filtered = redactor.FilterStackTrace(string.Join(
            '\n',
            "   at QueryGuard.EntityFrameworkCore.QueryGuardCommandInterceptor.ReaderExecuting()",
            "   at Microsoft.EntityFrameworkCore.Storage.RelationalCommand.ExecuteReader()",
            "   at System.Linq.Enumerable.ToList[T](IEnumerable`1 source)",
            "   at Contoso.Api.Companies.CompanyService.ListDepartments() in CompanyService.cs:line 42",
            "   at Contoso.Api.Companies.CompanyEndpoints.Get() in CompanyEndpoints.cs:line 17"));

        Assert.NotNull(filtered);
        Assert.DoesNotContain("QueryGuard.", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Linq", filtered, StringComparison.Ordinal);
        Assert.Contains("CompanyService.ListDepartments", filtered, StringComparison.Ordinal);
        Assert.Contains("CompanyEndpoints.Get", filtered, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trace_with_nothing_left_after_filtering_becomes_null_rather_than_empty()
    {
        // An empty trace looks like broken capture. Null says "nothing to show" unambiguously.
        var redactor = new QueryGuardRedactor();

        var filtered = redactor.FilterStackTrace(string.Join(
            '\n',
            "   at QueryGuard.Core.Something.Method()",
            "   at System.Threading.Tasks.Task.Execute()"));

        Assert.Null(filtered);
    }

    [Fact]
    public void Null_whitespace_and_empty_stack_traces_are_null()
    {
        var redactor = new QueryGuardRedactor();

        Assert.Null(redactor.FilterStackTrace(null));
        Assert.Null(redactor.FilterStackTrace(string.Empty));
        Assert.Null(redactor.FilterStackTrace("   \n  \n"));
    }

    [Fact]
    public void Clearing_the_frame_filters_keeps_every_frame()
    {
        var options = new QueryGuardCaptureOptions();
        options.StackTraceFrameFilters.Clear();
        var redactor = new QueryGuardRedactor(options);

        var filtered = redactor.FilterStackTrace("   at System.Threading.Tasks.Task.Execute()");

        Assert.NotNull(filtered);
        Assert.Contains("System.Threading", filtered, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_line_endings_in_a_stack_trace_are_handled()
    {
        var redactor = new QueryGuardRedactor();

        var filtered = redactor.FilterStackTrace(
            "   at Microsoft.EntityFrameworkCore.X.Y()\r\n   at Contoso.Api.Z.W()\r\n");

        Assert.NotNull(filtered);
        Assert.DoesNotContain("Microsoft.", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', filtered);
    }

    [Fact]
    public void Samples_are_trimmed_to_the_configured_limit_keeping_the_earliest()
    {
        // The earliest occurrences are the useful ones: the first execution is what a stack trace
        // would have pointed at.
        var redactor = new QueryGuardRedactor(new QueryGuardCaptureOptions { MaxSamplesPerFingerprint = 2 });
        var samples = Enumerable.Range(1, 10).Select(i => TestData.Record(sequence: i)).ToArray();

        var limited = redactor.LimitSamples<QueryRecord>(samples);

        Assert.Equal(2, limited.Count);
        Assert.Equal(1, limited[0].Sequence);
        Assert.Equal(2, limited[1].Sequence);
    }

    [Fact]
    public void A_sample_limit_of_zero_retains_counts_but_no_records()
    {
        var redactor = new QueryGuardRedactor(new QueryGuardCaptureOptions { MaxSamplesPerFingerprint = 0 });

        var limited = redactor.LimitSamples<QueryRecord>([TestData.Record()]);

        Assert.Empty(limited);
    }

    [Fact]
    public void A_sample_collection_within_the_limit_is_returned_unchanged()
    {
        var redactor = new QueryGuardRedactor();
        IReadOnlyList<QueryRecord> samples = [TestData.Record()];

        Assert.Same(samples, redactor.LimitSamples(samples));
    }

    [Fact]
    public void A_null_or_empty_sample_collection_is_handled()
    {
        var redactor = new QueryGuardRedactor();

        Assert.Empty(redactor.LimitSamples<QueryRecord>(null!));
        Assert.Empty(redactor.LimitSamples<QueryRecord>(Array.Empty<QueryRecord>()));
    }

    [Fact]
    public void Options_are_copied_so_a_later_mutation_cannot_change_capture_behavior()
    {
        // Options are configured at startup and read on the command path. A configuration object
        // mutated afterwards must not be able to widen capture halfway through a request.
        var options = new QueryGuardCaptureOptions { RedactStringLiterals = true };
        var redactor = new QueryGuardRedactor(options);

        options.RedactStringLiterals = false;
        options.StackTraceFrameFilters.Clear();

        var redacted = redactor.RedactSql("SELECT * FROM \"T\" WHERE \"Name\" = 'secret'");

        Assert.DoesNotContain("secret", redacted, StringComparison.Ordinal);
        Assert.NotEmpty(redactor.Options.StackTraceFrameFilters);
    }

    [Fact]
    public void A_redactor_can_be_constructed_without_options()
    {
        var redactor = new QueryGuardRedactor();

        Assert.False(redactor.Options.CaptureParameterValues);
    }

    [Theory]
    [InlineData(-1)]
    public void A_negative_sample_limit_is_rejected(int limit)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new QueryGuardCaptureOptions { MaxSamplesPerFingerprint = limit });

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_sql_length_limit_is_rejected(int limit)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new QueryGuardCaptureOptions { MaxNormalizedSqlLength = limit });

    [Fact]
    public void Cloning_produces_an_independent_copy()
    {
        var original = new QueryGuardCaptureOptions
        {
            CaptureParameterValues = true,
            CaptureFirstStackTrace = true,
            RedactNumericLiterals = false,
            MaxSamplesPerFingerprint = 7,
            MaxNormalizedSqlLength = 512,
        };
        original.StackTraceFrameFilters.Add("Contoso.Internal.");

        var copy = original.Clone();
        original.StackTraceFrameFilters.Clear();
        original.CaptureParameterValues = false;

        Assert.True(copy.CaptureParameterValues);
        Assert.True(copy.CaptureFirstStackTrace);
        Assert.False(copy.RedactNumericLiterals);
        Assert.Equal(7, copy.MaxSamplesPerFingerprint);
        Assert.Equal(512, copy.MaxNormalizedSqlLength);
        Assert.Contains("Contoso.Internal.", copy.StackTraceFrameFilters);
    }
}
