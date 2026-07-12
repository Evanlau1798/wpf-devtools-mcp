using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiVisualContractTests
{
    [Fact]
    public void TuiRenderer_ShouldDeclareViewportAnchoredRedrawContract()
    {
        var terminalContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1"));
        var rendererContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Renderer.ps1"));

        (terminalContent.Contains("SetCursorPosition") || terminalContent.Contains("[H"))
            .Should().BeTrue();
        terminalContent.Should().Contain("$script:TuiLastRenderedFrameHeight");
        terminalContent.Should().Contain("$script:TuiLastRenderedFrameWidth");
        rendererContent.Should().Contain("Write-TuiFrameCore");
    }

    [Fact]
    public void OnlineInstaller_HomeScreen_ShouldIgnoreOrphanedCustomInstallRootStateAndFallBackToAppData()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var stateDir = Path.Combine(appData, "WpfDevToolsMcp");
            var orphanedRoot = @"C:\Srv\Mcp";
            Directory.CreateDirectory(stateDir);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);
            File.WriteAllText(
                Path.Combine(stateDir, "installer-state.json"),
                """
                {
                  "schemaVersion": 1,
                  "lastInstallRoot": "__ORPHANED_ROOT__",
                  "architectures": {},
                  "registrations": {}
                }
                """.Replace("__ORPHANED_ROOT__", orphanedRoot.Replace("\\", "\\\\")));

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("AppData");
            result.Stdout.Should().Contain("WpfDevToolsMcp");
            result.Stdout.Should().NotContain(orphanedRoot);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

}
