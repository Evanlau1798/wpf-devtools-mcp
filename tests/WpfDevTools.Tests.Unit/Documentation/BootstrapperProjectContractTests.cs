using System.Xml.Linq;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class BootstrapperProjectContractTests
{
    [Fact]
    public void BootstrapperProject_ShouldIncludeArm64Configurations()
    {
        var project = XDocument.Load(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj"));

        var configurations = project
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectConfiguration")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        configurations.Should().Contain("Debug|ARM64");
        configurations.Should().Contain("Release|ARM64");
    }

    [Fact]
    public void BootstrapperProject_ShouldPrepareNetHostAssetsWithoutExternalVariables()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj"));

        content.Should().Contain("PrepareNetHostAssets",
            "the native bootstrapper build should self-resolve host headers/libs instead of requiring manual NetHostIncludeDir setup");
        content.Should().Contain("artifacts\\dotnet-host",
            "resolved nethost assets should be copied into a stable repo-local location for reproducible builds");
    }

    [Fact]
    public void BootstrapperProject_DebugBuild_ShouldUseLibNethostCompatibleRuntime()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj"));

        content.Should().NotContain("<RuntimeLibrary>MultiThreadedDebug</RuntimeLibrary>",
            "libnethost.lib is shipped as a release static library, so the bootstrapper cannot link against the debug CRT");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
