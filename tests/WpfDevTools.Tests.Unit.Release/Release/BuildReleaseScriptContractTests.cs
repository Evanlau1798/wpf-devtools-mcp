using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class ReleasePackagingContractTests
{
    [Fact]
    public void BuildReleaseScript_ShouldExistAsPublicPackagingEntryPoint()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1");

        File.Exists(scriptPath).Should().BeTrue(
            "maintainers should have a stable packaging entrypoint under scripts/tools");
    }

    [Fact]
    public void BuildReleaseScript_ShouldDelegateToPublishReleaseScript()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1");
        var content = File.ReadAllText(scriptPath);

        content.Should().Contain("packaging\\Publish-Release.ps1");
        content.Should().Contain("release");
    }

    [Fact]
    public void BuildReleaseScript_ShouldAllowPublishScriptOverrideForDeterministicScriptTests()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptRoot = Path.Combine(tempRoot, "scripts");
            Directory.CreateDirectory(scriptRoot);

            var copiedBuildScript = Path.Combine(scriptRoot, "build-release.ps1");
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1"), copiedBuildScript, overwrite: true);

            var publishLog = Path.Combine(tempRoot, "publish-log.json");
            var fakePublishScript = Path.Combine(tempRoot, "fake-publish.ps1");
            var outputRoot = Path.Combine(tempRoot, "custom-release");
            File.WriteAllText(
                fakePublishScript,
                string.Join(
                    Environment.NewLine,
                    [
                        "param(",
                        "    [string]$Configuration,",
                        "    [string[]]$Architectures,",
                        "    [string]$OutputRoot,",
                        "    [switch]$SkipBuild",
                        ")",
                        "$payload = @{",
                        "    Configuration = $Configuration",
                        "    Architectures = $Architectures",
                        "    OutputRoot = $OutputRoot",
                        "    SkipBuild = $SkipBuild.IsPresent",
                        "} | ConvertTo-Json -Depth 3",
                        $"Set-Content -Path '{publishLog.Replace("'", "''")}' -Value $payload -Encoding UTF8"
                    ]));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                copiedBuildScript,
                ["-Configuration", "Debug", "-Architectures", "x64", "-OutputRoot", outputRoot, "-SkipBuild"],
                new Dictionary<string, string?> { ["WPFDEVTOOLS_BUILD_RELEASE_PUBLISH_SCRIPT"] = fakePublishScript });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.Exists(publishLog).Should().BeTrue();

            using var document = JsonDocument.Parse(File.ReadAllText(publishLog));
            document.RootElement.GetProperty("Configuration").GetString().Should().Be("Debug");
            document.RootElement.GetProperty("Architectures").EnumerateArray().Select(x => x.GetString()).Should().Equal("x64");
            document.RootElement.GetProperty("OutputRoot").GetString().Should().Be(outputRoot);
            document.RootElement.GetProperty("SkipBuild").GetBoolean().Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void BuildReleaseScript_WhenOverridePublishScriptFails_ShouldSurfacePublishExitCode()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptRoot = Path.Combine(tempRoot, "scripts");
            Directory.CreateDirectory(scriptRoot);

            var copiedBuildScript = Path.Combine(scriptRoot, "build-release.ps1");
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1"), copiedBuildScript, overwrite: true);

            var fakePublishScript = Path.Combine(tempRoot, "failing-publish.ps1");
            File.WriteAllText(fakePublishScript, "exit 23");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                copiedBuildScript,
                Array.Empty<string>(),
                new Dictionary<string, string?> { ["WPFDEVTOOLS_BUILD_RELEASE_PUBLISH_SCRIPT"] = fakePublishScript });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("Release build failed with exit code 23");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void BuildReleaseScript_ShouldNormalizeCommaSeparatedArchitectureInput()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptRoot = Path.Combine(tempRoot, "scripts");
            Directory.CreateDirectory(scriptRoot);

            var copiedBuildScript = Path.Combine(scriptRoot, "build-release.ps1");
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1"), copiedBuildScript, overwrite: true);

            var publishLog = Path.Combine(tempRoot, "publish-architectures.json");
            var fakePublishScript = Path.Combine(tempRoot, "fake-publish.ps1");
            File.WriteAllText(
                fakePublishScript,
                string.Join(
                    Environment.NewLine,
                    [
                        "param(",
                        "    [string]$Configuration,",
                        "    [string[]]$Architectures,",
                        "    [string]$OutputRoot,",
                        "    [switch]$SkipBuild",
                        ")",
                        ("@{ Architectures = $Architectures } | ConvertTo-Json -Depth 3 | " +
                         $"Set-Content -Path '{publishLog.Replace("'", "''")}' -Encoding UTF8")
                    ]));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                copiedBuildScript,
                ["-Architectures", "x64,x86,arm64", "-SkipBuild"],
                new Dictionary<string, string?> { ["WPFDEVTOOLS_BUILD_RELEASE_PUBLISH_SCRIPT"] = fakePublishScript });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var document = JsonDocument.Parse(File.ReadAllText(publishLog));
            document.RootElement.GetProperty("Architectures").EnumerateArray().Select(x => x.GetString())
                .Should().Equal("x64", "x86", "arm64");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_ShouldBuildArchitectureIndependentAssembliesOnce()
    {
        var script = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));
        var architectureLoop = script.IndexOf(
            "foreach ($architecture in $resolvedArchitectures)",
            StringComparison.Ordinal);

        architectureLoop.Should().BeGreaterThan(0);
        script.IndexOf("'build', $inspectorProject", StringComparison.Ordinal)
            .Should().BeLessThan(architectureLoop);
        script.LastIndexOf("'build', $inspectorProject", StringComparison.Ordinal)
            .Should().BeLessThan(architectureLoop);
        script.IndexOf("'build', $inspectorSdkProject", StringComparison.Ordinal)
            .Should().BeLessThan(architectureLoop);
    }
}
