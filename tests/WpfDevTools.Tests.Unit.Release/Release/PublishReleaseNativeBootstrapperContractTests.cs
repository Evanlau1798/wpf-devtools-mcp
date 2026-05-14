using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release.Release;

public sealed class PublishReleaseNativeBootstrapperContractTests
{
    [Fact]
    public void PublishReleaseScript_ShouldDisableNativeBootstrapperIncrementalLinking()
    {
        var script = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        script.Should().Contain("/p:LinkIncremental=false",
            "CI and Windows Sandbox release packaging should not use Debug incremental native linking");
    }

    [Fact]
    public void PublishReleaseScript_ShouldPassNativeToolchainEnvironmentToBootstrapperMsBuild()
    {
        var script = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        script.Should().Contain("ConvertTo-MSBuildPropertyValue");
        script.Should().Contain("/p:WindowsSDKDir=$windowsSdkDirectory");
        script.Should().Contain("/p:WindowsTargetPlatformVersion=$windowsSdkVersion");
        script.Should().Contain("/p:IncludePath=$includePath");
        script.Should().Contain("/p:LibraryPath=$libraryPath");
        script.Should().Contain("/p:ExecutablePath=$executablePath");
    }

    [Fact]
    public void PublishReleaseScript_ShouldOnlyInjectInheritedNativePathsForX64BootstrapperBuilds()
    {
        var script = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        script.Should().Contain("if ($bootstrapperPlatform -eq 'x64')",
            "Win32 and ARM64 builds must not inherit x64 LIB/PATH entries from a hosted x64 developer shell");
        script.Should().Contain("/p:LibraryPath=$libraryPath");
        script.Should().Contain("/p:ExecutablePath=$executablePath");
    }
}
