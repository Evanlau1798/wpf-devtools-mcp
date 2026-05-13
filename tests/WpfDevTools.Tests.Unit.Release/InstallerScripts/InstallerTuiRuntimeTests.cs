using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class InstallerTuiRuntimeTests
{
    [Fact]
    public void TuiScreenModel_ShouldReuseDetectedRegistrationMapAcrossRefreshes()
    {
        var screenModelContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1"));
        var stateContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.State.ps1"));
        var flowContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Flow.ps1"));

        stateContent.Should().Contain("Get-TuiClientItems -State $state.InstallerState -Mode 'install' -RegistrationMap $detectedRegistrationMap");
        stateContent.Should().Contain("Get-TuiClientItems -State $state.InstallerState -Mode 'uninstall' -RegistrationMap $detectedRegistrationMap");
        stateContent.Should().Contain("Get-TuiClientItems -State $InstallerState -Mode 'install' -RegistrationMap $detectedRegistrationMap");
        stateContent.Should().Contain("Get-TuiClientItems -State $InstallerState -Mode 'uninstall' -RegistrationMap $detectedRegistrationMap");
        stateContent.Should().Contain("Get-TuiUpdateBannerText -State $state.InstallerState -LatestVersion $LatestVersion -RegistrationMap $detectedRegistrationMap");
        stateContent.Should().Contain("Get-TuiUpdateBannerText -State $InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $detectedRegistrationMap");
        screenModelContent.Should().NotContain("Get-TuiClientItems -State $state.InstallerState -Mode 'install' -RegistrationMap $detectedRegistrationMap");
        flowContent.Should().Contain("Get-AvailableInstallerUpdates -State $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap");
        flowContent.Should().Contain("Get-TuiUpdateBannerText -State $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap");
    }

    [Fact]
    public void TuiFlow_ShouldRebuildHomeItemsWhenBackgroundLatestVersionRefreshCompletesWithoutVersionPayload()
    {
        var flowContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Flow.ps1"));

        flowContent.Should().Contain("if ([string]::IsNullOrWhiteSpace([string]$refreshResult.Version))");
        flowContent.Should().Contain("$State.HomeItems = @(Get-TuiHomeItemsCore");
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
            var timeoutMarker = Path.Combine(tempRoot, "claude-timeout-marker.log");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(fakeBin);

            File.WriteAllText(
                Path.Combine(fakeBin, "claude.cmd"),
                "@echo off" + Environment.NewLine +
                "if \"%1 %2\"==\"mcp list\" (" + Environment.NewLine +
                "  powershell -NoProfile -Command \"Start-Sleep -Seconds 3; Set-Content -Path '" + timeoutMarker.Replace("'", "''") + "' -Value done\"" + Environment.NewLine +
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
                "$env:PATH='" + BuildShimOnlyPath(fakeBin).Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "$env:WPFDEVTOOLS_INSTALLER_VERIFICATION_TIMEOUT_SEC='1'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Installation Manager");

            System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(3500));
            File.Exists(timeoutMarker).Should().BeFalse("timed out CLI discovery should terminate the spawned process tree before the child marker is written");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void TuiClientAvailability_ShouldFindCodexPowerShellShimWhenCommandDiscoveryOmitsExternalScripts()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fakeBin = Path.Combine(tempRoot, "bin");
            Directory.CreateDirectory(fakeBin);
            File.WriteAllText(Path.Combine(fakeBin, "codex.ps1"), "exit 0" + Environment.NewLine);

            var screenModelPath = ReleaseScriptTestHarness.GetRepoFilePath(Path.Combine("scripts", "installer", "Tui.ScreenModel.ps1"));
            var command = string.Join(" ; ",
            [
                "function Get-Command { param([Parameter(ValueFromRemainingArguments=$true)][object[]]$Arguments) return $null }",
                ". '" + screenModelPath.Replace("'", "''") + "'",
                "$env:PATH='" + BuildShimOnlyPath(fakeBin).Replace("'", "''") + "'",
                "$state = [pscustomobject]@{ registrations = @{} }",
                "if (-not (Test-TuiClientAvailable -ClientId 'codex' -State $state)) { throw 'codex shim was not detected' }"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_TuiStartup_ShouldDetectCodexViaPowerShellShim()
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
            var detectedExecutable = Path.Combine(tempRoot, "external", "wpf-devtools-x64.exe");

            File.WriteAllText(
                Path.Combine(fakeBin, "codex.ps1"),
                "param([Parameter(ValueFromRemainingArguments=$true)][string[]]$args)" + Environment.NewLine +
                "if (($args -join ' ') -eq 'mcp list') { Write-Output 'wpf-devtools " + detectedExecutable.Replace("'", "''") + "'; exit 0 }" + Environment.NewLine +
                "exit 0" + Environment.NewLine);

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:PATH='" + BuildShimOnlyPath(fakeBin).Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='DownArrow||Enter||Escape||Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "$env:WPFDEVTOOLS_INSTALLER_VERIFICATION_TIMEOUT_SEC='5'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Select what to uninstall");
            result.Stdout.Should().Contain("Codex/Codex CLI");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_TuiStartup_ShouldNotThrowWhenProgramFilesX86IsMissing()
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
                "[Environment]::SetEnvironmentVariable('ProgramFiles(x86)', $null, 'Process')",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x86 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Installation Manager");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string BuildShimOnlyPath(string fakeBin)
        => string.Join(
            Path.PathSeparator,
            [
                fakeBin,
                Environment.SystemDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0")
            ]);
}
