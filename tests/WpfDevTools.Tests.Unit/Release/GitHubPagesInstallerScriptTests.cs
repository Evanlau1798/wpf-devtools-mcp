using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class GitHubPagesInstallerScriptTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldInstallFromLocalArchiveViaPackageSetup()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile,
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("installedExecutable").GetString()
                .Should().EndWith("x64\\current\\WpfDevTools.Mcp.Server.exe");
            File.Exists(Path.Combine(installRoot, "x64", "current", "WpfDevTools.Mcp.Server.exe")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldUseRequestedArchitectureForPackageMetadata()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
            {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "arm64");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-Architecture", "arm64",
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["PROCESSOR_ARCHITECTURE"] = "ARM64",
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("installedExecutable").GetString()
                .Should().Contain("arm64\\current\\WpfDevTools.Mcp.Server.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldSurfaceReleaseArchiveMetadata()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot, "x64");
            File.WriteAllText(
                Path.Combine(packageDir, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    name = "wpf-devtools",
                    version = "1.2.3",
                    architecture = "x64",
                    runtimeId = "win-x64",
                    channel = "dev",
                    buildConfiguration = "Debug",
                    signaturePolicy = "DebugTrustedRootSkip"
                }));
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/release/Install-WpfDevTools.ps1"), Path.Combine(packageDir, "install.ps1"), overwrite: true);
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/release/Setup-WpfDevTools.ps1"), Path.Combine(packageDir, "setup.ps1"), overwrite: true);
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/release/Uninstall-WpfDevTools.ps1"), Path.Combine(packageDir, "uninstall.ps1"), overwrite: true);

            var archivePath = Path.Combine(tempRoot, "release_1.2.3_win-x64.zip");
            ZipFile.CreateFromDirectory(packageDir, archivePath);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-Version", "1.2.3",
                    "-Architecture", "x64",
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("version").GetString().Should().Be("1.2.3");
            json.RootElement.GetProperty("architecture").GetString().Should().Be("x64");
            json.RootElement.GetProperty("packageAssetName").GetString().Should().Be("release_1.2.3_win-x64.zip");
            json.RootElement.GetProperty("downloadUri").GetString().Should().Contain("github.com/Evanlau1798/wpf-devtools-mcp/releases/download/1.2.3/release_1.2.3_win-x64.zip");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
