using System;
using Xunit;

namespace QueryGuard.Tests;

/// <summary>
/// Parsing a source location out of a captured stack trace.
/// </summary>
/// <remarks>
/// The consequence of getting this wrong is not an exception, it is an annotation on the wrong line of
/// the wrong file — so the cases that matter most are the ones where it must decline to answer.
/// </remarks>
public class QueryGuardOriginTests
{
    [Fact]
    public void A_frame_with_a_file_and_line_is_parsed()
    {
        Assert.True(QueryGuardOrigin.TryParse(
            "at Sample.Api.Endpoints.List(AppDbContext db) in C:\\repo\\src\\Endpoints.cs:line 89",
            out var origin));

        Assert.NotNull(origin);
        Assert.Equal("C:\\repo\\src\\Endpoints.cs", origin!.FilePath);
        Assert.Equal(89, origin.Line);
        Assert.Equal("Sample.Api.Endpoints.List(AppDbContext db)", origin.Callable);
    }

    [Fact]
    public void The_first_frame_that_has_a_location_wins_even_if_it_is_not_the_first_frame()
    {
        // The nearest frame is often a compiler-generated state machine with no symbols of its own,
        // while the frame behind it is the code somebody wrote.
        var trace = string.Join(
            '\n',
            "at Sample.Api.Endpoints.<List>b__0(AppDbContext db)",
            "at Sample.Api.Endpoints.List(AppDbContext db) in /repo/src/Endpoints.cs:line 89");

        Assert.True(QueryGuardOrigin.TryParse(trace, out var origin));
        Assert.Equal(89, origin!.Line);
    }

    [Fact]
    public void A_trace_with_no_symbols_yields_nothing()
    {
        // A normal state, not an error: the query has an origin, there is just nothing to point at.
        Assert.False(QueryGuardOrigin.TryParse(
            "at Sample.Api.Endpoints.List(AppDbContext db)",
            out var origin));

        Assert.Null(origin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void Nothing_useful_yields_nothing(string? trace)
    {
        Assert.False(QueryGuardOrigin.TryParse(trace, out var origin));
        Assert.Null(origin);
    }

    [Fact]
    public void A_path_containing_the_line_separator_is_split_on_the_last_one()
    {
        // A directory may legitimately contain ":line ". Splitting on the first occurrence would cut the
        // path in half and parse the rest of it as a number.
        Assert.True(QueryGuardOrigin.TryParse(
            "at T.M() in /repo/weird:line dir/File.cs:line 12",
            out var origin));

        Assert.Equal("/repo/weird:line dir/File.cs", origin!.FilePath);
        Assert.Equal(12, origin.Line);
    }

    [Theory]
    [InlineData("at T.M() in /repo/File.cs:line 0")]
    [InlineData("at T.M() in /repo/File.cs:line -3")]
    [InlineData("at T.M() in /repo/File.cs:line abc")]
    [InlineData("at T.M() in :line 12")]
    public void An_unusable_location_is_declined_rather_than_guessed(string frame)
    {
        // Zero and negative are not lines, a non-number is not a line, and an empty path is not a file.
        // Emitting any of them would put an annotation somewhere arbitrary.
        Assert.False(QueryGuardOrigin.TryParse(frame, out var origin));
        Assert.Null(origin);
    }

    [Fact]
    public void A_frame_without_the_leading_at_still_parses()
    {
        // The redactor's filtered output is not contractually prefixed, so requiring "at " would make
        // this depend on a detail of another component's formatting.
        Assert.True(QueryGuardOrigin.TryParse("T.M() in /repo/File.cs:line 7", out var origin));
        Assert.Equal(7, origin!.Line);
    }

    [Fact]
    public void ToString_reads_the_way_the_assertion_message_prints_it()
    {
        Assert.True(QueryGuardOrigin.TryParse("at T.M() in /repo/File.cs:line 7", out var origin));
        Assert.Equal("/repo/File.cs:line 7", origin!.ToString());
    }

    [Fact]
    public void Carriage_returns_do_not_end_up_in_the_path()
    {
        // A trace captured on Windows arrives with CRLF, and a trailing \r in a URI is invisible in a
        // report and breaks the match against a diff.
        Assert.True(QueryGuardOrigin.TryParse(
            "at T.M() in /repo/File.cs:line 7\r\nat Other.N()",
            out var origin));

        Assert.Equal("/repo/File.cs", origin!.FilePath);
        Assert.DoesNotContain("\r", origin.FilePath, StringComparison.Ordinal);
    }
}
