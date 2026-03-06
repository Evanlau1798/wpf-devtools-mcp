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

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
