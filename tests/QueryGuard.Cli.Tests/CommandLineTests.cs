using Xunit;

namespace QueryGuard.Cli.Tests;

/// <summary>
/// Argument parsing.
/// </summary>
/// <remarks>
/// Hand-written parsing is a reasonable choice for two commands and five options, and it is only
/// reasonable if it is tested. The failure mode that matters is a silently ignored typo: a misspelt
/// <c>--baseline</c> would write to the default path and the run would look like it worked.
/// </remarks>
public class CommandLineTests
{
    [Fact]
    public void Nothing_at_all_is_a_usage_error()
    {
        Assert.False(CommandLine.TryParse([], out _, out var error));
        Assert.Contains("No command", error!, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_two_word_command_is_joined()
    {
        Assert.True(CommandLine.TryParse(["baseline", "record"], out var parsed, out _));

        Assert.Equal("baseline record", parsed!.Command);
    }

    [Fact]
    public void Baseline_without_a_subcommand_says_which_one_exists()
    {
        Assert.False(CommandLine.TryParse(["baseline"], out _, out var error));
        Assert.Contains("record", error!, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_followed_by_an_option_is_still_a_missing_subcommand()
    {
        // Otherwise "--reports" becomes the subcommand and the error blames the wrong thing.
        Assert.False(CommandLine.TryParse(["baseline", "--reports", "x"], out _, out var error));
        Assert.Contains("subcommand", error!, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Options_take_the_following_token_as_a_value()
    {
        Assert.True(CommandLine.TryParse(
            ["verify", "--reports", "artifacts/qg", "--baseline", "base.json"],
            out var parsed,
            out _));

        Assert.Equal("artifacts/qg", parsed!.Option("reports", "default"));
        Assert.Equal("base.json", parsed.Option("baseline", "default"));
    }

    [Fact]
    public void An_option_with_no_value_is_a_flag()
    {
        Assert.True(CommandLine.TryParse(["verify", "--fail-on-regression"], out var parsed, out _));

        Assert.True(parsed!.HasFlag("fail-on-regression"));
        Assert.Null(parsed.OptionOrNull("fail-on-regression"));
    }

    [Fact]
    public void A_flag_before_an_option_does_not_swallow_it()
    {
        // The parser decides "flag or option" by looking ahead for a token that does not start with
        // "--". Getting this wrong would make --fail-on-regression eat the next option's name.
        Assert.True(CommandLine.TryParse(
            ["verify", "--fail-on-regression", "--baseline", "base.json"],
            out var parsed,
            out _));

        Assert.True(parsed!.HasFlag("fail-on-regression"));
        Assert.Equal("base.json", parsed.Option("baseline", "default"));
    }

    [Fact]
    public void A_missing_option_falls_back()
        => Assert.Equal("fallback", Parse("verify").Option("reports", "fallback"));

    [Fact]
    public void A_positional_argument_is_rejected_rather_than_ignored()
    {
        Assert.False(CommandLine.TryParse(["verify", "surprise"], out _, out var error));
        Assert.Contains("surprise", error!, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_double_dash_is_rejected()
        => Assert.False(CommandLine.TryParse(["verify", "--"], out _, out _));

    [Fact]
    public void An_unknown_option_is_named()
    {
        // The whole point: a typo has to be reported, not absorbed. A misspelt --baseline that silently
        // wrote to the default path would be a wrong answer that looks like a right one.
        var parsed = Parse("verify", "--baselien", "base.json");

        Assert.Equal("baselien", parsed.FindUnknown("reports", "baseline"));
    }

    [Fact]
    public void An_unknown_flag_is_named()
        => Assert.Equal("dry-run", Parse("verify", "--dry-run").FindUnknown("reports", "baseline"));

    [Fact]
    public void Known_options_and_flags_report_nothing_unknown()
        => Assert.Null(
            Parse("verify", "--reports", "x", "--fail-on-regression")
                .FindUnknown("reports", "baseline", "fail-on-regression"));

    private static CommandLine Parse(params string[] args)
    {
        Assert.True(CommandLine.TryParse(args, out var parsed, out var error), error);
        return parsed!;
    }
}
