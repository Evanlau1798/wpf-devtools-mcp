using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiRuntimeTests
{
    [Fact]
    public void TuiScreenModel_ShouldReuseDetectedRegistrationMapAcrossRefreshes()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1"));

        content.Should().Contain("Get-TuiClientItems -State $state.InstallerState -Mode 'install' -RegistrationMap $detectedRegistrationMap");
        content.Should().Contain("Get-TuiClientItems -State $state.InstallerState -Mode 'uninstall' -RegistrationMap $detectedRegistrationMap");
        content.Should().Contain("Get-TuiClientItems -State $InstallerState -Mode 'install' -RegistrationMap $detectedRegistrationMap");
        content.Should().Contain("Get-TuiClientItems -State $InstallerState -Mode 'uninstall' -RegistrationMap $detectedRegistrationMap");
    }

    [Fact]
    public void OnlineInstaller_TuiStartup_ShouldBoundCliDiscoveryTimeouts()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var fakeBin = Path.Combine(tempRoot, "bin");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(fakeBin);

            File.WriteAllText(
                Path.Combine(fakeBin, "claude.cmd"),
                "@echo off" + Environment.NewLine +
                "if \"%1 %2\"==\"mcp list\" (" + Environment.NewLine +
                "  powershell -NoProfile -Command \"Start-Sleep -Seconds 5\"" + Environment.NewLine +
                "  echo wpf-devtools" + Environment.NewLine +
                "  exit /b 0" + Environment.NewLine +
                ")" + Environment.NewLine +
                "exit /b 0" + Environment.NewLine);

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:PATH='" + fakeBin.Replace("'", "''") + ";" + Environment.GetEnvironmentVariable("PATH")!.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "$env:WPFDEVTOOLS_INSTALLER_VERIFICATION_TIMEOUT_SEC='1'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var stopwatch = Stopwatch.StartNew();
            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);
            stopwatch.Stop();

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("HomeScreen");
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
