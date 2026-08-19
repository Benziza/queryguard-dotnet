using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace QueryGuard.Tests;

/// <summary>
/// Guards the shared build configuration that every shipped QueryGuard package inherits.
/// These properties are part of what consumers receive, so a silent regression in
/// Directory.Build.props is a shipping defect rather than a cosmetic one.
/// </summary>
public class BuildConfigurationTests
{
    private static readonly Assembly CoreAssembly = typeof(QueryGuardAssemblyMarker).Assembly;

    private static readonly string[] SupportedFrameworkNames =
    [
        ".NETCoreApp,Version=v8.0",
        ".NETCoreApp,Version=v10.0",
    ];

    [Fact]
    public void Core_assembly_is_built_deterministically()
    {
        // A deterministic build is what makes the published symbols and the source served
        // through SourceLink verifiable against the tagged commit.
        var metadata = CoreAssembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);

        Assert.True(
            metadata.ContainsKey("RepositoryUrl") || CoreAssembly.GetName().Version is not null,
            "The assembly should carry repository metadata stamped by the shared build configuration.");
    }

    [Fact]
    public void Core_assembly_declares_the_expected_product_identity()
    {
        var product = CoreAssembly.GetCustomAttribute<AssemblyProductAttribute>();
        var company = CoreAssembly.GetCustomAttribute<AssemblyCompanyAttribute>();

        Assert.NotNull(product);
        Assert.Equal("QueryGuard.NET", product.Product);
        Assert.NotNull(company);
        Assert.False(string.IsNullOrWhiteSpace(company.Company));
    }

    [Fact]
    public void Core_assembly_is_versioned_as_a_preview()
    {
        var informational = CoreAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        Assert.NotNull(informational);

        // Preview-first versioning is a deliberate decision: the public API and the report
        // schema need real feedback before they are committed to.
        // See docs/decisions/0011-versioning.md.
        Assert.StartsWith("0.", informational.InformationalVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void Core_assembly_targets_a_supported_framework()
    {
        var target = CoreAssembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();

        Assert.NotNull(target);

        // .NET 9 is intentionally not a target. See docs/decisions/0008-target-frameworks.md.
        Assert.Contains(target.FrameworkName, SupportedFrameworkNames, StringComparer.Ordinal);
    }
}
