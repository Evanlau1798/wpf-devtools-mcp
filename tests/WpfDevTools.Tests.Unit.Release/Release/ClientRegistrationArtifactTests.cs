using System.Text.Json;
using FluentAssertions;
using Xunit;
using static WpfDevTools.Tests.Unit.Release.ClientRegistrationArtifactTestSupport;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class ClientRegistrationArtifactInstallTests
{
    [Fact]
    public void OnlineInstaller_ShouldWriteVsCodeRegistrationToConfiguredJsonFile()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");
            JsonRegistrationTestAssertions.SeedRegistrationFile(vscodeConfigPath, "servers", "existing", "old.exe");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-VsCodeConfigPath", vscodeConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllText(vscodeConfigPath)
                .Should().Contain("\"servers\"")
                .And.Contain("existing")
                .And.Contain("wpf-devtools-x64.exe");
            Directory.GetFiles(Path.GetDirectoryName(vscodeConfigPath)!, "mcp.json.bak-*").Should().NotBeEmpty();

            using var json = JsonDocument.Parse(result.Stdout);
            var registration = json.RootElement.GetProperty("registrations").EnumerateArray().Single();
            registration.GetProperty("mode").GetString().Should().Be("json-file");
            registration.GetProperty("applied").GetBoolean().Should().BeTrue();
            registration.GetProperty("target").GetString().Should().Be(vscodeConfigPath);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldWriteVisualStudioRegistrationToUserProfileConfig()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var visualStudioConfigPath = Path.Combine(tempRoot, "UserProfile", ".mcp.json");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllText(visualStudioConfigPath)
                .Should().Contain("\"servers\"")
                .And.Contain("wpf-devtools-x64.exe");

            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
                .Should().BeEquivalentTo(["visual-studio"]);
            json.RootElement.GetProperty("registrations").EnumerateArray().Select(x => x.GetProperty("mode").GetString())
                .Should().OnlyContain(mode => mode == "json-file");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldRemoveJsonRegistrationFromConfiguredFiles()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");
            JsonRegistrationTestAssertions.SeedRegistrationFile(vscodeConfigPath, "servers", "existing", "old.exe");

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-VsCodeConfigPath", vscodeConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));
            install.ExitCode.Should().Be(0, install.Stderr);

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-VsCodeConfigPath", vscodeConfigPath,
                    "-NonInteractive",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot));

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            JsonRegistrationTestAssertions.AssertRegistrationAbsent(vscodeConfigPath, "servers", "wpf-devtools");
            JsonRegistrationTestAssertions.AssertRegistrationCommand(vscodeConfigPath, "servers", "existing", "old.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
