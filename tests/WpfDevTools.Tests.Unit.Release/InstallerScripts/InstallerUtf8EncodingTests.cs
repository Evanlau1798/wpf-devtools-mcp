using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerUtf8EncodingTests
{
    [Fact]
    public void SaveInstallerState_ShouldWriteUtf8WithoutBom()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var statePath = Path.Combine(appData, "WpfDevToolsMcp", "installer-state.json");
            Directory.CreateDirectory(appData);

            var command = string.Join(" ; ",
            [
                ". " + QuotePowerShellString(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.State.ps1")),
                "$state = Get-EmptyInstallerState",
                "$state.lastInstallRoot = " + QuotePowerShellString(Path.Combine(tempRoot, "install-root")),
                "$path = Save-InstallerState -State $state",
                "Write-Output $path"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            AssertDoesNotStartWithUtf8Bom(statePath);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SetJsonConfigRegistration_ShouldWriteUtf8WithoutBom()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(tempRoot, "config", "mcp.json");
            var executablePath = Path.Combine(tempRoot, "bin", "wpf-devtools.exe");
            var command = string.Join(" ; ",
            [
                ". " + QuotePowerShellString(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1")),
                "$null = Set-JsonConfigRegistration -ClientName 'vscode' -CollectionName 'servers' -ConfigPath " + QuotePowerShellString(configPath) + " -InstalledExecutable " + QuotePowerShellString(executablePath),
                "Write-Output " + QuotePowerShellString(configPath)
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            AssertDoesNotStartWithUtf8Bom(configPath);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void NewClientRegistrationArtifacts_ShouldWriteJsonArtifactsWithoutBom()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installBase = Path.Combine(tempRoot, "install-base");
            var executablePath = Path.Combine(tempRoot, "current", "bin", "wpf-devtools.exe");
            var command = string.Join(" ; ",
            [
                ". " + QuotePowerShellString(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Registration.ps1")),
                "New-ClientRegistrationArtifacts -InstallBase " + QuotePowerShellString(installBase) + " -InstalledExecutable " + QuotePowerShellString(executablePath)
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            AssertDoesNotStartWithUtf8Bom(Path.Combine(installBase, "client-registration", "vscode.json"));
            AssertDoesNotStartWithUtf8Bom(Path.Combine(installBase, "client-registration", "cursor.global.json"));
            AssertDoesNotStartWithUtf8Bom(Path.Combine(installBase, "client-registration", "claude-desktop.json"));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldWriteInstallManifestWithoutBom()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            AssertDoesNotStartWithUtf8Bom(Path.Combine(installRoot, "x64", "install-manifest.json"));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static void AssertDoesNotStartWithUtf8Bom(string path)
    {
        var bytes = File.ReadAllBytes(path);
        bytes.Take(3).Should().NotEqual([0xEF, 0xBB, 0xBF], "installer JSON/config outputs must be UTF-8 without BOM");
    }

    private static string QuotePowerShellString(string value)
        => "'" + value.Replace("'", "''") + "'";
}
