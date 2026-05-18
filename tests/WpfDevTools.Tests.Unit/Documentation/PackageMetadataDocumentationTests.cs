using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class PackageMetadataDocumentationTests
{
    [Fact]
    public void InspectorSdkProject_ShouldDeclarePackageReadmeFile()
    {
        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj"));

        content.Should().Contain("<PackageReadmeFile>README.md</PackageReadmeFile>");
    }

    [Fact]
    public void InspectorSdkPackageReadme_ShouldExistAndDescribeOptInUsage()
    {
        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Inspector.Sdk/README.md"));

        content.Should().Contain("Opt-in SDK");
        content.Should().Contain("without DLL injection");
        content.Should().Contain("WPF DevTools MCP Server");
    }

    [Fact]
    public void InspectorSdkPackageReadme_ShouldUsePublicationAwareInstallGuidance()
    {
        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Inspector.Sdk/README.md"));

        content.Should().NotContain(
            "dotnet add package WpfDevTools.Inspector.Sdk\n",
            "the SDK package is not currently discoverable from NuGet.org, so the README should not over-promise public package availability");
        content.Should().Contain("dotnet pack src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj");
        content.Should().Contain("--source");
        content.Should().Contain("After public NuGet publication");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
