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
    public void InstallBatchTemplate_ShouldBootstrapPackagedInstallerWithElevationAndExecutionPolicyBypass()
    {
        var batchTemplatePath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/run-template.bat");
        var content = File.ReadAllText(batchTemplatePath);

        File.Exists(batchTemplatePath).Should().BeTrue();
        content.Should().Contain("bin\\install.ps1");
        content.Should().Contain("powershell.exe");
        content.Should().Contain("-ExecutionPolicy Bypass");
        content.Should().Contain("RunAs");
    }

    [Fact]
    public void PublishReleaseScript_ShouldCopyCanonicalInstallerAndAvoidLegacyScriptChain()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        content.Should().Contain("run-template.bat");
        content.Should().Contain("run.bat");
        content.Should().Contain("scripts\\online-installer.ps1");
        content.Should().Contain("bin\\install.ps1");
        content.Should().NotContain("Setup-WpfDevTools.ps1");
        content.Should().NotContain("internal-install.ps1");
        content.Should().NotContain("Uninstall-WpfDevTools.ps1");
    }

    [Fact]
    public void PublishReleaseScript_ShouldUseVersionedReleaseArchiveNames()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        content.Should().Contain("release_${version}_win-$architecture.zip");
        content.Should().NotContain("_dev_win-");
    }

    [Fact]
    public void PublishReleaseScript_ShouldRetryArchiveCreationWhenTransientFileLocksOccur()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        content.Should().Contain("Invoke-ArchiveCreation");
        content.Should().Contain("Compress-Archive");
        content.Should().Contain("Start-Sleep");
    }

    [Fact]
    public void PublishReleaseScript_ShouldFallbackToFrameworkBuildOutputForSkipBuildPackaging()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        content.Should().Contain("Copy-ServerBuildOutput");
        content.Should().Contain("Layout = 'framework'");
        content.Should().Contain("WpfDevTools.Mcp.Server.exe");
        content.Should().Contain("Join-Path $SourceInfo.Path 'runtimes'");
    }

    [Fact]
    public void PackageLocalInstaller_ShouldInstallServerExecutableFromBinDirectory()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var packageLocalScript = Path.Combine(packageDir, "bin", "install.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                packageLocalScript,
                overwrite: true);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                packageLocalScript,
                ["-InstallRoot", installRoot, "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.Exists(Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_ShouldFailFastWhenArm64ToolchainIsUnavailable()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fakeVsRoot = Path.Combine(tempRoot, "vs", "MSBuild", "Current", "Bin");
            Directory.CreateDirectory(fakeVsRoot);
            var fakeMsbuildPath = Path.Combine(fakeVsRoot, "MSBuild.exe");
            File.WriteAllText(fakeMsbuildPath, "stub");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"),
                ["-Configuration", "Debug", "-Architectures", "arm64", "-SkipBuild", "-OutputRoot", Path.Combine(tempRoot, "release-output")],
                new Dictionary<string, string?> { ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuildPath });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("ARM64 bootstrapper build requires");
            result.Stderr.Should().Contain("Microsoft.VisualStudio.Component.VC.Tools.ARM64");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
