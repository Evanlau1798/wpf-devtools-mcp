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
    public void InspectorSdkPackageReadmeInstallCommandCheck_ShouldRejectCrLfBarePublicInstallCommand()
    {
        var content = "```bash\r\ndotnet add package WpfDevTools.Inspector.Sdk\r\n```";

        ContainsBarePublicSdkInstallCommand(content).Should().BeTrue(
            "the package README guard must not be bypassed by CRLF line endings");
    }

    [Fact]
    public void InspectorSdkPackageReadme_ShouldUsePublicationAwareInstallGuidance()
    {
        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Inspector.Sdk/README.md"));

        ContainsBarePublicSdkInstallCommand(content).Should().BeFalse(
            "the SDK package is not currently discoverable from NuGet.org, so the README should not over-promise public package availability");
        content.Should().Contain("dotnet pack src/WpfDevTools.Inspector.Sdk/WpfDevTools.Inspector.Sdk.csproj");
        content.Should().Contain("--source");
        content.Should().Contain("After public NuGet publication");
    }

    private static bool ContainsBarePublicSdkInstallCommand(string content)
        => content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Any(line => line.Trim() == "dotnet add package WpfDevTools.Inspector.Sdk");

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
