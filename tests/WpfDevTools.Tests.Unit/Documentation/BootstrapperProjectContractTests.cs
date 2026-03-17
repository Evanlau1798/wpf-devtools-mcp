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

    [Fact]
    public void BootstrapperProject_ShouldFallbackToLatestInstalledToolsetWhenV143PlatformToolsetIsUnavailable()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj"));

        content.Should().Contain("PlatformToolset",
            "the bootstrapper project should keep an explicit native toolset contract");
        content.Should().Contain("v145",
            "Visual Studio 18 contributors should be able to build release packages without manually retargeting the project");
        content.Should().Contain("v143",
            "the project should keep backward compatibility with Visual Studio 2022 build environments");
        content.Should().Contain("PlatformToolsets\\v145\\Toolset.props",
            "toolset fallback should be based on installed MSBuild platform toolset discovery instead of a hard-coded developer machine assumption");
    }

    [Fact]
    public void BootstrapperProject_ShouldResolveNetFxSdkLibrariesForArm64Packaging()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/WpfDevTools.Bootstrapper.vcxproj"));

        content.Should().Contain("NetFxSdkLibraryDir",
            "ARM64 packaging should not rely on implicit linker search paths to find mscoree.lib");
        content.Should().Contain("$(NETFXKitsDir)Lib\\um\\arm",
            "the installed .NET Framework SDK ships ARM libraries under Lib\\\\um\\\\arm, not Lib\\\\um\\\\arm64");
        content.Should().Contain("$(NetFxSdkLibraryDir)",
            "the bootstrapper linker inputs should include the explicit NetFx SDK library fallback directory");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
