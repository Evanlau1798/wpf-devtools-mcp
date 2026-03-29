using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiRuntimeTests
{
    [Fact]
    public void TuiScreenModel_ShouldReuseDetectedRegistrationMapAcrossRefreshes()
    {
        var screenModelContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1"));
        var flowContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Flow.ps1"));

        screenModelContent.Should().Contain("Get-TuiClientItems -State $state.InstallerState -Mode 'install' -RegistrationMap $detectedRegistrationMap");
        screenModelContent.Should().Contain("Get-TuiClientItems -State $state.InstallerState -Mode 'uninstall' -RegistrationMap $detectedRegistrationMap");
        screenModelContent.Should().Contain("Get-TuiClientItems -State $InstallerState -Mode 'install' -RegistrationMap $detectedRegistrationMap");
        screenModelContent.Should().Contain("Get-TuiClientItems -State $InstallerState -Mode 'uninstall' -RegistrationMap $detectedRegistrationMap");
        screenModelContent.Should().Contain("Get-TuiUpdateBannerText -State $state.InstallerState -LatestVersion $LatestVersion -RegistrationMap $detectedRegistrationMap");
        screenModelContent.Should().Contain("Get-TuiUpdateBannerText -State $InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $detectedRegistrationMap");
        flowContent.Should().Contain("Get-AvailableInstallerUpdates -State $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap");
        flowContent.Should().Contain("Get-TuiUpdateBannerText -State $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap");
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
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));

            System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(3500));
            File.Exists(timeoutMarker).Should().BeFalse("timed out CLI discovery should terminate the spawned process tree before the child marker is written");
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

            File.WriteAllText(
                Path.Combine(fakeBin, "codex.ps1"),
                "param([Parameter(ValueFromRemainingArguments=$true)][string[]]$args)" + Environment.NewLine +
                "if (($args -join ' ') -eq 'mcp list') { Write-Output 'wpf-devtools'; exit 0 }" + Environment.NewLine +
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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='DownArrow||Enter||Escape||Escape'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "$env:WPFDEVTOOLS_INSTALLER_VERIFICATION_TIMEOUT_SEC='1'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("UninstallScreen");
            result.Stdout.Should().Contain("Codex");
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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x86 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("HomeScreen");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_TuiStartup_ShouldShowUpdateBannerForDetectedInstallerOwnedRegistrationWithoutState()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var executablePath = Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe");
            var installBase = Path.Combine(installRoot, "x64");
            var configPath = Path.Combine(appData, "Code", "User", "mcp.json");

            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            File.WriteAllText(executablePath, "stub");
            File.WriteAllText(
                Path.Combine(installBase, "install-manifest.json"),
                """
                {
                  "name": "wpf-devtools",
                  "architecture": "x64",
                  "version": "1.0.0",
                  "installRoot": "__INSTALL_ROOT__",
                  "installDir": "__INSTALL_DIR__",
                  "executable": "__EXECUTABLE__"
                }
                """
                .Replace("__INSTALL_ROOT__", installRoot.Replace("\\", "\\\\"))
                .Replace("__INSTALL_DIR__", Path.Combine(installBase, "current").Replace("\\", "\\\\"))
                .Replace("__EXECUTABLE__", executablePath.Replace("\\", "\\\\")));
            File.WriteAllText(
                configPath,
                """
                {
                  "servers": {
                    "wpf-devtools": {
                      "command": "__EXECUTABLE__",
                      "args": []
                    }
                  }
                }
                """
                .Replace("__EXECUTABLE__", executablePath.Replace("\\", "\\\\")));

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

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Update available");
            result.Stdout.Should().Contain("1 target(s) can move to v1.2.3");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_TuiStartup_ShouldRefreshUpdateBannerWithoutCachedLatestVersion()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var executablePath = Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe");
            var installBase = Path.Combine(installRoot, "x64");
            var configPath = Path.Combine(appData, "Code", "User", "mcp.json");

            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            File.WriteAllText(executablePath, "stub");
            File.WriteAllText(
                Path.Combine(installBase, "install-manifest.json"),
                """
                {
                  "name": "wpf-devtools",
                  "architecture": "x64",
                  "version": "1.0.0",
                  "installRoot": "__INSTALL_ROOT__",
                  "installDir": "__INSTALL_DIR__",
                  "executable": "__EXECUTABLE__"
                }
                """
                .Replace("__INSTALL_ROOT__", installRoot.Replace("\\", "\\\\"))
                .Replace("__INSTALL_DIR__", Path.Combine(installBase, "current").Replace("\\", "\\\\"))
                .Replace("__EXECUTABLE__", executablePath.Replace("\\", "\\\\")));
            File.WriteAllText(
                configPath,
                """
                {
                  "servers": {
                    "wpf-devtools": {
                      "command": "__EXECUTABLE__",
                      "args": []
                    }
                  }
                }
                """
                .Replace("__EXECUTABLE__", executablePath.Replace("\\", "\\\\")));

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Tick||Tick||Tick||Escape'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Update available");
            result.Stdout.Should().Contain("1 target(s) can move to v1.2.3");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_HomeScreen_ShouldDisplayCurrentInstallLocation()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var installRoot = @"C:\Srv\Mcp";
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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='100'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='30'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other -InstallRoot '" + installRoot.Replace("'", "''") + "'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Install location");
            result.Stdout.Should().Contain(installRoot);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_TuiRenderer_ShouldRespectNarrowConsoleWidth()
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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='72'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='22'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            System.Text.RegularExpressions.Regex
                .Split(result.Stdout, @"\r\n|\n|\r")
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Select(static line => line.Length)
                .Should().OnlyContain(static length => length <= 72);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldVerifyCodexRegistrationViaPowerShellShim()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var codexLog = Path.Combine(tempRoot, "codex.log");
            Directory.CreateDirectory(fakeBin);

            File.WriteAllText(
                Path.Combine(fakeBin, "codex.ps1"),
                "param([Parameter(ValueFromRemainingArguments=$true)][string[]]$args)" + Environment.NewLine +
                "Add-Content -Path '" + codexLog.Replace("'", "''") + "' -Value ($args -join ' ')" + Environment.NewLine +
                "if (($args[0] -eq 'mcp') -and ($args[1] -eq 'list')) { Write-Output 'wpf-devtools'; exit 0 }" + Environment.NewLine +
                "if (($args[0] -eq 'mcp') -and ($args[1] -eq 'add')) { exit 0 }" + Environment.NewLine +
                "exit 0" + Environment.NewLine);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "codex", "-NonInteractive", "-Force", "-OutputJson"],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = BuildShimOnlyPath(fakeBin),
                    ["WPFDEVTOOLS_INSTALLER_VERIFICATION_TIMEOUT_SEC"] = "1"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllText(codexLog)
                .Should().Contain("mcp add wpf-devtools")
                .And.Contain("mcp list");
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
