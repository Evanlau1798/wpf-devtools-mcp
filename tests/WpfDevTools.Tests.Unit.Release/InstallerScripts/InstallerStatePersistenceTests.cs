using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerStatePersistenceTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeclareStateInstallationHelper()
    {
        var content = ReleaseScriptTestHarness.GetOnlineInstallerSourceBundle();
        var manifestContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json"));

        content.Should().Contain("scripts/installer/Installer.State.Installation.ps1");
        manifestContent.Should().Contain("Installer.State.Installation.ps1");
        content.IndexOf(
                "scripts/installer/Installer.State.ps1",
                StringComparison.Ordinal)
            .Should()
            .BeLessThan(content.IndexOf(
                "scripts/installer/Installer.State.Installation.ps1",
                StringComparison.Ordinal));
        content.IndexOf(
                "'Installer.State.Installation.ps1'",
                StringComparison.Ordinal)
            .Should()
            .BeLessThan(content.IndexOf(
                "'Installer.Registration.ps1'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void OnlineInstaller_ShouldPersistSharedInstallerStateUnderRoamingAppData()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64");
            var installRoot = Path.Combine(tempRoot, "shared-server-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            Directory.CreateDirectory(appData);

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
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);

            var statePath = Path.Combine(appData, "WpfDevToolsMcp", "installer-state.json");
            File.Exists(statePath).Should().BeTrue("the installer should persist the chosen install root for reuse");

            using var state = JsonDocument.Parse(File.ReadAllText(statePath));
            state.RootElement.GetProperty("lastInstallRoot").GetString().Should().Be(installRoot);
            state.RootElement.GetProperty("architectures").GetProperty("x64").GetProperty("installRoot").GetString()
                .Should().Be(installRoot);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerFullUninstall_WhenLastInstallerOwnedInstallIsRemoved_ShouldClearLastInstallRoot()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64");
            var installRoot = Path.Combine(tempRoot, "shared-server-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var environment = new Dictionary<string, string?>
            {
                ["APPDATA"] = appData,
                ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                ["PATH"] = string.Empty
            };

            var installResult = ReleaseScriptTestHarness.RunPowerShellScript(
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
                environment);

            installResult.ExitCode.Should().Be(0, installResult.Stderr);

            var uninstallResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-Action", "full-uninstall",
                    "-InstallRoot", installRoot,
                    "-Architecture", "x64",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                environment);

            uninstallResult.ExitCode.Should().Be(0, uninstallResult.Stderr);
            using var uninstallPayload = JsonDocument.Parse(uninstallResult.Stdout);
            uninstallPayload.RootElement.GetProperty("removedInstallation").GetBoolean().Should().BeTrue();

            var statePath = Path.Combine(appData, "WpfDevToolsMcp", "installer-state.json");
            if (!File.Exists(statePath))
            {
                return;
            }

            using var state = JsonDocument.Parse(File.ReadAllText(statePath));
            state.RootElement.GetProperty("lastInstallRoot").ValueKind.Should().Be(JsonValueKind.Null);
            state.RootElement.GetProperty("architectures").EnumerateObject().Should().BeEmpty();
            state.RootElement.GetProperty("registrations").EnumerateObject().Should().BeEmpty();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SharedInstallerStateLoader_ShouldQuarantineMalformedStateAndReturnEmptyState()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var stateDirectory = Path.Combine(appData, "WpfDevToolsMcp");
            Directory.CreateDirectory(stateDirectory);
            var statePath = Path.Combine(stateDirectory, "installer-state.json");
            File.WriteAllText(statePath, "{ this is not valid json", System.Text.Encoding.UTF8);

            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.State.ps1").Replace("'", "''") + "'",
                "$state = Get-InstallerState",
                "@{ State = $state; StateExists = (Test-Path -LiteralPath '" + statePath.Replace("'", "''") + "'); CorruptCount = @((Get-ChildItem -LiteralPath '" + stateDirectory.Replace("'", "''") + "' -Filter 'installer-state.json.corrupt-*' -ErrorAction SilentlyContinue)).Count } | ConvertTo-Json -Depth 5 -Compress"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
                });

            result.ExitCode.Should().Be(0, result.Stderr);

            using var payload = JsonDocument.Parse(result.Stdout);
            payload.RootElement.GetProperty("StateExists").GetBoolean().Should().BeFalse(
                "malformed shared installer state should be quarantined instead of hard-failing future installer flows");
            payload.RootElement.GetProperty("CorruptCount").GetInt32().Should().Be(1);
            payload.RootElement.GetProperty("State").GetProperty("lastInstallRoot").ValueKind.Should().Be(JsonValueKind.Null);
            payload.RootElement.GetProperty("State").GetProperty("architectures").EnumerateObject().Should().BeEmpty();
            payload.RootElement.GetProperty("State").GetProperty("registrations").EnumerateObject().Should().BeEmpty();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
