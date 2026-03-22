using System.IO;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("PackagingIntegration")]
public sealed class ReleasePackagingIntegrationTests
{
    [Fact]
    public void BuildReleaseScript_ShouldProduceVersionedPackageWithBinLayout()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var result = ReleasePackagingTestHarness.RunPowerShellScript(
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1"),
                new[] { "-Configuration", "Debug", "-Architectures", "x64", "-OutputRoot", outputRoot, "-SkipBuild" });

            result.ExitCode.Should().Be(0, result.Stderr);
            var archivePath = Directory.GetFiles(outputRoot, "release_*_win-x64.zip").Single();
            var extractRoot = ReleasePackagingTestHarness.ExtractArchive(archivePath, tempRoot);

            File.Exists(Path.Combine(extractRoot, "run.bat")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "install.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.ScreenModel.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Renderer.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Input.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Flow.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Confirm.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Installer.Discovery.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Installer.Uninstall.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "manifest.json")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "wpf-devtools-x64.exe")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "internal-install.ps1")).Should().BeFalse(
                "the simplified package should not ship a second PowerShell installer chain");

            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(extractRoot, "bin", "manifest.json")));
            manifest.RootElement.GetProperty("entryExecutable").GetString()
                .Should().Be("bin/wpf-devtools-x64.exe");
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void BuildReleaseScript_MultiArchitecturePackaging_ShouldNotLeakForeignRidInspectorOutputs()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var escapedOutputRoot = outputRoot.Replace("'", "''");
            var command = "& '" +
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1").Replace("'", "''") +
                "' -Configuration Debug -Architectures @('x64','x86') -OutputRoot '" + escapedOutputRoot + "'";
            var result = ReleasePackagingTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);

            var x86ArchivePath = Directory.GetFiles(outputRoot, "release_*_win-x86.zip").Single();
            var extractRoot = ReleasePackagingTestHarness.ExtractArchive(x86ArchivePath, Path.Combine(tempRoot, "x86-extract"));
            var inspectorNet8Root = Path.Combine(extractRoot, "bin", "inspectors", "net8.0-windows");

            Directory.Exists(Path.Combine(inspectorNet8Root, "win-x64")).Should().BeFalse(
                "the x86 package must not include RID-specific inspector output from a previous x64 publish");
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void BuildReleaseScript_ReleaseMode_ShouldProduceX64X86AndArm64Archives()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var command = "& '" +
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/tools/build-release.ps1").Replace("'", "''") +
                "' -Configuration Release -Architectures @('x64','x86','arm64') -OutputRoot '" +
                outputRoot.Replace("'", "''") + "'";
            var result = ReleasePackagingTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            Directory.GetFiles(outputRoot, "release_*_win-x64.zip").Should().ContainSingle();
            Directory.GetFiles(outputRoot, "release_*_win-x86.zip").Should().ContainSingle();
            var arm64ArchivePath = Directory.GetFiles(outputRoot, "release_*_win-arm64.zip").Should().ContainSingle().Subject;
            var extractRoot = ReleasePackagingTestHarness.ExtractArchive(arm64ArchivePath, Path.Combine(tempRoot, "arm64-extract"));

            File.Exists(Path.Combine(extractRoot, "run.bat")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "install.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.ScreenModel.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Confirm.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Installer.Discovery.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Installer.Uninstall.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "wpf-devtools-arm64.exe")).Should().BeTrue();
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PublishReleaseScript_WhenBootstrapperStepFails_ShouldCleanPartialArm64PackageDirectory()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var fakeMsbuildLog = Path.Combine(tempRoot, "fake-msbuild.log");
            var fakeMsbuild = ReleasePackagingTestHarness.CreateFakeCommand(tempRoot, "fake-msbuild", fakeMsbuildLog, "exit /b 1");

            var result = ReleasePackagingTestHarness.RunPowerShellScript(
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"),
                new[] { "-Configuration", "Debug", "-Architectures", "arm64", "-OutputRoot", outputRoot },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuild
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("arm64",
                "the failure should identify which release architecture could not be packaged");
            Directory.Exists(Path.Combine(outputRoot, "release_0.1.0_win-arm64")).Should().BeFalse(
                "failed packaging should clean partial package directories so later retries start from a clean slate");
            File.Exists(Path.Combine(outputRoot, "release_0.1.0_win-arm64.zip")).Should().BeFalse();
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
