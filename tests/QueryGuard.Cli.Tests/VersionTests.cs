using System.Text.RegularExpressions;
using Xunit;

namespace QueryGuard.Cli.Tests;

/// <summary>
/// What <c>--version</c> reports.
/// </summary>
/// <remarks>
/// Worth a test because the wrong answer here is quiet and expensive: every preview shares the assembly
/// version <c>0.1.0.0</c>, so a bug report quoting it cannot say which build it came from, and nothing
/// about the output looks wrong. Asserted against the shape rather than a literal, so cutting a release
/// does not require editing a test.
/// </remarks>
public class VersionTests
{
    [Fact]
    public void The_reported_version_is_not_the_four_part_assembly_version()
    {
        Assert.DoesNotMatch(@"^\d+\.\d+\.\d+\.\d+$", Program.Version());
    }

    [Fact]
    public void The_reported_version_is_a_semantic_version()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+.+)?$", Program.Version());
    }

    [Fact]
    public void The_reported_version_identifies_the_commit()
    {
        // SourceLink stamps the commit into the informational version. It is the part of a bug report
        // that says exactly which build produced the behaviour, so losing it should fail here.
        Assert.Matches(@"\+[0-9a-f]{40}$", Program.Version());
    }
}
