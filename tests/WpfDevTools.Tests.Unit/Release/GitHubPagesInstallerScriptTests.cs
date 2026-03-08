using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class GitHubPagesInstallerScriptTests
{
    [Fact]
    public void GitHubPagesInstaller_ShouldInstallFromLocalArchiveViaPackageSetup()
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
                ReleaseScriptTestHarness.GetRepoFilePath("docfx/install.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Clients", "none",
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
    public void GitHubPagesInstaller_ShouldDefaultToHostArchitectureWhenArchitectureIsOmitted()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "arm64");
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("docfx/install.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-Clients", "none",
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
    public void GitHubPagesInstaller_ShouldSurfaceDevelopmentChannelMetadata()
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

            var archivePath = Path.Combine(tempRoot, "WpfDevTools-dev-win-x64.zip");
            ZipFile.CreateFromDirectory(packageDir, archivePath);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("docfx/install.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-Channel", "dev",
                    "-Clients", "none",
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
            json.RootElement.GetProperty("channel").GetString().Should().Be("dev");
            json.RootElement.GetProperty("buildConfiguration").GetString().Should().Be("Debug");
            json.RootElement.GetProperty("packageAssetName").GetString().Should().Be("WpfDevTools-dev-win-x64.zip");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
