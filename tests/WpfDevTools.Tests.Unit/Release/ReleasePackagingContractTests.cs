using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleasePackagingContractTests
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
        File.Exists(scriptPath).Should().BeTrue();

        var content = File.ReadAllText(scriptPath);

        content.Should().Contain("release\\Publish-Release.ps1",
            "the public packaging entrypoint should reuse the existing release publishing logic");
        content.Should().Contain("release",
            "the packaging entrypoint should target the release output area by default");
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
                string.Join(Environment.NewLine,
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
                new[] { "-Configuration", "Debug", "-Architectures", "x64", "-OutputRoot", outputRoot, "-SkipBuild" },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_BUILD_RELEASE_PUBLISH_SCRIPT"] = fakePublishScript
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.Exists(publishLog).Should().BeTrue("the build wrapper should honor the publish-script override for isolated script tests");

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
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_BUILD_RELEASE_PUBLISH_SCRIPT"] = fakePublishScript
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("Release build failed with exit code 23");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InstallBatchTemplate_ShouldExistAsPackageEntryPoint()
    {
        var batchTemplatePath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/run-template.bat");

        File.Exists(batchTemplatePath).Should().BeTrue(
            "downloaded release packages should expose a batch entrypoint for users who cannot execute .ps1 directly");
    }

    [Fact]
    public void PublishReleaseScript_ShouldCopyBatchInstallerIntoPackageRoot()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Publish-Release.ps1"));

        content.Should().Contain("run-template.bat",
            "the packaged release root should include run.bat as the offline installer entrypoint");
        content.Should().Contain("run.bat",
            "the release publisher should emit a batch installer at the package root");
    }

    [Fact]
    public void PublishReleaseScript_ShouldUseVersionedReleaseArchiveNames()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Publish-Release.ps1"));

        content.Should().Contain("release_${version}_win-$architecture.zip",
            "GitHub release assets should use the new versioned naming contract");
        content.Should().NotContain("_dev_win-",
            "release archive names should stay stable regardless of Debug or Release packaging mode");
    }

    [Fact]
    public void InstallScript_ShouldInstallServerExecutableFromBinDirectory()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = Path.Combine(tempRoot, "package");
            var installRoot = Path.Combine(tempRoot, "install-root");
            Directory.CreateDirectory(Path.Combine(packageDir, "bin"));
            File.WriteAllText(Path.Combine(packageDir, "bin", "WpfDevTools.Mcp.Server.exe"), "stub");
            File.WriteAllText(
                Path.Combine(packageDir, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    name = "wpf-devtools",
                    version = "1.2.3",
                    architecture = "x64",
                    runtimeId = "win-x64"
                }));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Install-WpfDevTools.ps1"),
                new[] { "-PackagePath", packageDir, "-InstallRoot", installRoot, "-Force" });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.Exists(Path.Combine(installRoot, "x64", "current", "bin", "WpfDevTools.Mcp.Server.exe"))
                .Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
