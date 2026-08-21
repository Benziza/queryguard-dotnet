using System;
using Xunit;

namespace QueryGuard.Tests;

public class SqlNormalizerTests
{
    private readonly SqlNormalizer _normalizer = new();

    [Fact]
    public void Whitespace_and_line_breaks_collapse_to_single_spaces()
    {
        var normalized = _normalizer.Normalize("SELECT\n\t  1,\r\n   2  \n FROM   \"T\"");

        Assert.Equal("SELECT 1, 2 FROM \"T\"", normalized);
    }

    [Fact]
    public void The_same_query_formatted_differently_normalizes_identically()
    {
        var multiLine = _normalizer.Normalize(ProviderSqlFixtures.SqliteDepartmentsByCompany);
        var singleLine = _normalizer.Normalize(ProviderSqlFixtures.SqliteDepartmentsByCompanySingleLine);

        Assert.Equal(multiLine, singleLine);
    }

    [Fact]
    public void Leading_and_trailing_whitespace_is_removed()
    {
        var normalized = _normalizer.Normalize("   SELECT 1   ");

        Assert.Equal("SELECT 1", normalized);
    }

    [Fact]
    public void Null_empty_and_whitespace_only_input_becomes_an_empty_string()
    {
        Assert.Equal(string.Empty, _normalizer.Normalize(null));
        Assert.Equal(string.Empty, _normalizer.Normalize(string.Empty));
        Assert.Equal(string.Empty, _normalizer.Normalize("   \n\t "));
    }

    [Theory]
    [InlineData("@__companyId_0")]
    [InlineData("@__companyId_137")]
    [InlineData("@p0")]
    [InlineData(":companyId")]
    [InlineData("?")]
    public void Every_parameter_syntax_becomes_the_same_placeholder(string parameter)
    {
        var normalized = _normalizer.Normalize($"SELECT 1 FROM \"T\" WHERE \"Id\" = {parameter}");

        Assert.Equal("SELECT 1 FROM \"T\" WHERE \"Id\" = ?", normalized);
    }

    [Fact]
    public void Positional_postgres_parameters_become_the_same_placeholder()
    {
        var first = _normalizer.Normalize("SELECT 1 WHERE \"Id\" = $1");
        var third = _normalizer.Normalize("SELECT 1 WHERE \"Id\" = $3");

        Assert.Equal(first, third);
        Assert.EndsWith("= ?", first, StringComparison.Ordinal);
    }

    [Fact]
    public void A_postgres_cast_operator_is_not_mistaken_for_a_parameter()
    {
        // `::integer` starts with a colon. Treating it as a named parameter would delete the cast and
        // silently merge queries that differ by type.
        var normalized = _normalizer.Normalize("SELECT \"Id\"::integer FROM \"T\"");

        Assert.Contains("::integer", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_dollar_sign_is_not_a_parameter()
    {
        var normalized = _normalizer.Normalize("SELECT 'a $ b' FROM \"T\"");

        Assert.Contains("'a $ b'", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordinary_comments_are_removed()
    {
        var normalized = _normalizer.Normalize("""
            -- reviewed by the platform team
            SELECT 1 /* inline note */ FROM "T"
            """);

        Assert.DoesNotContain("platform team", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("inline note", normalized, StringComparison.Ordinal);
        Assert.Equal("SELECT 1 FROM \"T\"", normalized);
    }

    [Fact]
    public void A_queryguard_directive_survives_comment_removal()
    {
        // The one comment that changes behavior has to survive, which also means a tagged query is a
        // distinct fingerprint: a call site the author chose to single out.
        var normalized = _normalizer.Normalize(ProviderSqlFixtures.SqliteTaggedIgnore);

        Assert.Contains("QueryGuard:Ignore", normalized, StringComparison.Ordinal);
        Assert.Contains("reason=bounded-reference-lookup", normalized, StringComparison.Ordinal);
        Assert.Contains("FROM \"Companies\"", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_comment_directive_does_not_comment_out_the_statement()
    {
        // The directive arrives as a line comment - TagWith always emits one - and this pass collapses
        // the line break that terminated it. Left as "--" the rest of the normalized text sits inside
        // the comment, and every reporter prints that text, so a tagged query showed SQL that reads as
        // entirely commented out. Asserting the delimiter rather than just the substring, because
        // Contains("QueryGuard:Ignore") passed throughout the bug.
        var normalized = _normalizer.Normalize(ProviderSqlFixtures.SqliteTaggedIgnore);

        Assert.DoesNotContain("--", normalized, StringComparison.Ordinal);
        Assert.Contains("/*", normalized, StringComparison.Ordinal);
        Assert.Contains("*/", normalized, StringComparison.Ordinal);

        // The statement has to survive outside the comment, not merely appear in the string.
        var afterComment = normalized[(normalized.IndexOf("*/", StringComparison.Ordinal) + 2)..];
        Assert.Contains("SELECT", afterComment, StringComparison.Ordinal);
        Assert.Contains("FROM \"Companies\"", afterComment, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_directive_written_either_way_normalizes_identically()
    {
        // How the comment was delimited is not part of what the query does, so the two spellings must
        // not produce two fingerprints for one call site.
        var line = _normalizer.Normalize("-- QueryGuard:Ignore reason=polling\nSELECT 1");
        var block = _normalizer.Normalize("/* QueryGuard:Ignore reason=polling */ SELECT 1");

        Assert.Equal(line, block);
    }

    [Fact]
    public void A_queryguard_directive_in_a_block_comment_also_survives()
    {
        var normalized = _normalizer.Normalize("/* QueryGuard:Ignore reason=polling */ SELECT 1");

        Assert.Contains("QueryGuard:Ignore", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tagged_query_and_the_same_query_untagged_normalize_differently()
    {
        var tagged = _normalizer.Normalize(ProviderSqlFixtures.SqliteTaggedIgnore);
        var untagged = _normalizer.Normalize(ProviderSqlFixtures.SqliteTaggedHumanNote);

        Assert.NotEqual(tagged, untagged);
    }

    [Fact]
    public void An_unterminated_block_comment_does_not_leave_text_behind()
    {
        var normalized = _normalizer.Normalize("SELECT 1 /* never closed");

        Assert.Equal("SELECT 1", normalized);
    }

    [Fact]
    public void A_comment_marker_inside_a_string_literal_is_not_a_comment()
    {
        // Treating `--` inside a literal as a comment would silently delete the rest of the
        // statement, producing a fingerprint for SQL that was never executed.
        var normalized = _normalizer.Normalize("SELECT 'a -- b' FROM \"T\" WHERE \"Id\" = @p0");

        Assert.Contains("'a -- b'", normalized, StringComparison.Ordinal);
        Assert.Contains("WHERE", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void An_escaped_quote_inside_a_literal_does_not_end_it_early()
    {
        var normalized = _normalizer.Normalize("SELECT 'O''Brien -- not a comment' FROM \"T\"");

        Assert.Contains("'O''Brien -- not a comment'", normalized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"Departments\"")]
    [InlineData("[Departments]")]
    [InlineData("`Departments`")]
    public void Quoted_identifiers_are_left_exactly_as_written(string identifier)
    {
        var normalized = _normalizer.Normalize($"SELECT * FROM {identifier}");

        Assert.Contains(identifier, normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void A_parameter_like_sequence_inside_a_quoted_identifier_is_preserved()
    {
        // A column genuinely named with an @ is unusual but legal, and rewriting it would corrupt
        // the statement.
        var normalized = _normalizer.Normalize("SELECT \"@odd_name\" FROM \"T\"");

        Assert.Contains("\"@odd_name\"", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Statement_terminators_and_the_sql_server_prologue_do_not_affect_grouping()
    {
        // EF Core emits `SET NOCOUNT ON;` and trailing semicolons inconsistently for the same logical
        // query. Grouping on them would split one query into several.
        var withPrologue = _normalizer.Normalize(ProviderSqlFixtures.SqlServerDepartmentsByCompany);
        var bare = _normalizer.Normalize(ProviderSqlFixtures.SqlServerDepartmentsByCompanyBareStatement);

        Assert.EndsWith("= ?", bare, StringComparison.Ordinal);
        Assert.Contains("SET NOCOUNT ON", withPrologue, StringComparison.Ordinal);

        // The prologue is a real difference in the statement, so it is not silently discarded, but
        // the statement that follows it normalizes the same way.
        Assert.EndsWith(bare, withPrologue, StringComparison.Ordinal);
    }

    [Fact]
    public void Token_order_is_never_changed()
    {
        var normalized = _normalizer.Normalize(ProviderSqlFixtures.SqliteDepartmentsReorderedColumns);

        // Sorting the column list would merge this with the canonically ordered query, and the report
        // would then point at SQL the application never ran.
        Assert.StartsWith("SELECT \"d\".\"Name\", \"d\".\"CompanyId\", \"d\".\"Id\"", normalized, StringComparison.Ordinal);
    }
}
