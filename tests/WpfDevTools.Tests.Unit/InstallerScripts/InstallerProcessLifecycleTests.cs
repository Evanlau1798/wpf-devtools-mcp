using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("TimingSensitive")]
public sealed class InstallerProcessLifecycleTests
{
    [Fact]
    public void ReleaseScriptTestHarness_RunPowerShellCommand_ShouldTimeoutAndKillProcessTree()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var markerPath = Path.Combine(tempRoot, "timed-out-child.marker");
            var childCommand = string.Join(" ; ",
            [
                "Start-Sleep -Seconds 3",
                "Set-Content -Path '" + markerPath.Replace("'", "''") + "' -Value done"
            ]);
            var command = "powershell -NoProfile -ExecutionPolicy Bypass -Command \"" + childCommand.Replace("\"", "`\"") + "\"";

            var act = () => ReleaseScriptTestHarness.RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(1));

            act.Should().Throw<TimeoutException>();
            Thread.Sleep(TimeSpan.FromMilliseconds(3500));
            File.Exists(markerPath).Should().BeFalse("the harness should kill the full PowerShell process tree when a command times out");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_TuiTestMode_ShouldFailFastWhenKeyQueueIsExhausted()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(10));

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("TUI test key queue exhausted");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void TuiFlow_ShouldDeclareLatestVersionRefreshTeardown()
    {
        var flowContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Flow.ps1"));
        var installerContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        flowContent.Should().Contain("Stop-TuiLatestVersionRefreshCore");
        installerContent.Should().Contain("Stop-LatestInstallerVersionRefresh");
    }
}
