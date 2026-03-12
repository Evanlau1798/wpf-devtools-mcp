using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleasePackagingContractTests
{
    [Fact]
    public void BuildReleaseScript_ShouldExistAsPublicPackagingEntryPoint()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/build-release.ps1");

        File.Exists(scriptPath).Should().BeTrue(
            "maintainers should have a stable packaging entrypoint outside scripts/release");
    }

    [Fact]
    public void BuildReleaseScript_ShouldDelegateToPublishReleaseScript()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/build-release.ps1");
        File.Exists(scriptPath).Should().BeTrue();

        var content = File.ReadAllText(scriptPath);

        content.Should().Contain("release\\Publish-Release.ps1",
            "the public packaging entrypoint should reuse the existing release publishing logic");
        content.Should().Contain("release",
            "the packaging entrypoint should target the release output area by default");
    }

    [Fact]
    public void InstallBatchTemplate_ShouldExistAsPackageEntryPoint()
    {
        var batchTemplatePath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/release/install-template.bat");

        File.Exists(batchTemplatePath).Should().BeTrue(
            "downloaded release packages should expose a batch entrypoint for users who cannot execute .ps1 directly");
    }

    [Fact]
    public void PublishReleaseScript_ShouldCopyBatchInstallerIntoPackageRoot()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/release/Publish-Release.ps1"));

        content.Should().Contain("install-template.bat",
            "the packaged release root should include install.bat alongside install.ps1");
        content.Should().Contain("install.bat",
            "the release publisher should emit a batch installer at the package root");
    }

    [Fact]
    public void PublishReleaseScript_ShouldUseVersionedReleaseArchiveNames()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/release/Publish-Release.ps1"));

        content.Should().Contain("release_${version}_win-$architecture.zip",
            "GitHub release assets should use the new versioned naming contract");
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
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/release/Install-WpfDevTools.ps1"),
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
