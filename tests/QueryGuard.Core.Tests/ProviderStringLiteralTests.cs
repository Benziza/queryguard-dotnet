using System;
using Xunit;

namespace QueryGuard.Tests;

public class ProviderStringLiteralTests
{
    [Theory]
    [InlineData("$$private_value$$")]
    [InlineData("$tag$private_value$tag$")]
    [InlineData("$_tag9$private_value$_tag9$")]
    [InlineData("$étiquette$private_value$étiquette$")]
    [InlineData("$ta\u0301g$private_value$ta\u0301g$")]
    [InlineData("$tag$private_value $other$ ' @p0 -- comment\n /* text */ $tag$")]
    [InlineData("$$private_value")]
    [InlineData("$tag$private_value$TAG$")]
    [InlineData("E'prefix\\'private_value'")]
    [InlineData("e'prefix\\'private_value -- @p0 /* text */'")]
    [InlineData("E'prefix\\'private_value")]
    [InlineData("E'prefix\\\\\\'private_value'")]
    [InlineData("'prefix\\'private_value'")]
    [InlineData("'\\', 'private_value'")]
    public void Provider_strings_do_not_retain_values(string literal)
    {
        var sql = "SELECT " + literal;
        var redactor = new QueryGuardRedactor();
        var redacted = redactor.RedactSql(sql);
        var fingerprint = new QueryFingerprintFactory().Create(sql, QueryCommandKind.Reader);

        Assert.DoesNotContain("private_value", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("private_value", fingerprint.NormalizedSql, StringComparison.Ordinal);
        Assert.Equal(redacted, redactor.RedactSql(redacted));
    }

    [Theory]
    [InlineData("$$private_value @p0 -- text\n /* text */ ';'$$")]
    [InlineData("$tag$private_value $other$ @p0$tag$")]
    [InlineData("E'prefix\\'private_value -- @p0 /* text */'")]
    [InlineData("E'prefix\\\\'", "SELECT E'?' FROM \"T\" WHERE \"Id\" = ?")]
    [InlineData("'prefix\\\\'", "SELECT '?' FROM \"T\" WHERE \"Id\" = ?")]
    public void Normalization_preserves_literal_boundaries_and_following_sql(
        string literal, string? expected = null)
    {
        var sql = $"SELECT {literal} FROM \"T\" WHERE \"Id\" = @p0;";
        var normalized = new SqlNormalizer().Normalize(sql);

        Assert.Equal($"SELECT {literal} FROM \"T\" WHERE \"Id\" = ?", normalized);
        var fingerprint = new QueryFingerprintFactory().Create(sql, QueryCommandKind.Reader);
        Assert.Equal(expected ?? (literal.StartsWith('E') ? "SELECT E'?'" : "SELECT '?'")
            + " FROM \"T\" WHERE \"Id\" = ?", fingerprint.NormalizedSql);
    }

    [Theory]
    [InlineData("$$private_value 123$$")]
    [InlineData("$tag$private_value 123$tag$")]
    [InlineData("E'prefix\\'private_value 123'")]
    public void Disabling_string_redaction_preserves_whole_literals(string literal)
    {
        var redactor = new QueryGuardRedactor(new QueryGuardCaptureOptions { RedactStringLiterals = false });

        Assert.Equal($"SELECT {literal}, ?", redactor.RedactSql($"SELECT {literal}, 42"));
    }

    [Theory]
    [InlineData("$$first$$", "$tag$second$tag$")]
    [InlineData("E'first\\'value'", "E'second\\'value'")]
    public void Changing_only_literal_values_keeps_the_fingerprint(string first, string second)
    {
        var factory = new QueryFingerprintFactory();

        Assert.Equal(factory.Create("SELECT " + first, QueryCommandKind.Reader).Id,
            factory.Create("SELECT " + second, QueryCommandKind.Reader).Id);
    }

    [Fact]
    public void Dollar_parameters_and_identifiers_keep_their_meaning()
    {
        var factory = new QueryFingerprintFactory();
        var fingerprint = factory.Create("SELECT column$tag$, $1, $23 FROM \"$tag$table\"", QueryCommandKind.Reader);

        Assert.Equal("SELECT column$tag$, ?, ? FROM \"$tag$table\"", fingerprint.NormalizedSql);
    }

    [Fact]
    public void An_ambiguous_backslash_quote_hides_both_sql_mode_interpretations()
    {
        var factory = new QueryFingerprintFactory();

        Assert.Equal("SELECT '?'", factory.Create(
            "SELECT '\\', 'private_value' FROM \"T\"", QueryCommandKind.Reader).NormalizedSql);
        Assert.Equal("SELECT '?'", factory.Create(
            "SELECT 'prefix\\'private_value' FROM \"T\"", QueryCommandKind.Reader).NormalizedSql);
    }
}
