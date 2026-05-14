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
    public void ResolveWindowsSdkVersion_ShouldIgnoreWdfIncludeDirectory()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var sdkDirectory = Path.Combine(tempRoot, "Windows Kits", "10");
            Directory.CreateDirectory(Path.Combine(sdkDirectory, "Include", "10.0.22621.0"));
            Directory.CreateDirectory(Path.Combine(sdkDirectory, "Include", "10.0.26100.0"));
            Directory.CreateDirectory(Path.Combine(sdkDirectory, "Include", "wdf"));
            var functionOnlyScript = CreateFunctionOnlyPublishReleaseScript(tempRoot);
            var command = $$"""
            . '{{EscapePowerShellPath(functionOnlyScript)}}'
            $actual = Resolve-WindowsSdkVersion -WindowsSdkDirectory '{{EscapePowerShellPath(sdkDirectory)}}'
            if ($actual -ne '10.0.26100.0') {
                throw "Expected numeric SDK version 10.0.26100.0 but resolved '$actual'."
            }
            """;

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr + result.Stdout);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
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

    private static string CreateFunctionOnlyPublishReleaseScript(string tempRoot)
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1");
        var functionLines = File.ReadLines(scriptPath)
            .TakeWhile(line => !line.StartsWith("$repoRoot =", StringComparison.Ordinal))
            .ToArray();
        var functionOnlyScript = Path.Combine(tempRoot, "Publish-Release.Functions.ps1");
        File.WriteAllLines(functionOnlyScript, functionLines);
        return functionOnlyScript;
    }

    private static string EscapePowerShellPath(string path)
        => path.Replace("'", "''", StringComparison.Ordinal);
}
