using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class InstallerTuiRuntimeTests
{
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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape||Enter'",
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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Tick||Tick||Tick||Escape||Enter'",
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
    public void OnlineInstaller_TuiStartup_ShouldNotShowUpdateBannerForExternalRegistrationBackedByStaleState()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var externalExecutable = Path.Combine(tempRoot, "external", "wpf-devtools-x64.exe");
            var configPath = Path.Combine(appData, "Code", "User", "mcp.json");
            var statePath = Path.Combine(appData, "WpfDevToolsMcp", "installer-state.json");
            var staleInstallRoot = Path.Combine(tempRoot, "stale-install-root");

            Directory.CreateDirectory(Path.GetDirectoryName(externalExecutable)!);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            File.WriteAllText(externalExecutable, "stub");
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
                .Replace("__EXECUTABLE__", externalExecutable.Replace("\\", "\\\\")));
            File.WriteAllText(
                statePath,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    lastInstallRoot = staleInstallRoot,
                    architectures = new Dictionary<string, object?>(),
                    registrations = new Dictionary<string, object?>
                    {
                        ["vscode"] = new
                        {
                            architecture = "x64",
                            installRoot = staleInstallRoot,
                            mode = "json-file",
                            target = configPath,
                            resolvedVersion = "1.0.0",
                            installedExecutable = externalExecutable,
                            lastVerifiedUtc = "2026-04-16T00:00:00.0000000Z"
                        }
                    }
                }));

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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().NotContain("Update available");
            result.Stdout.Should().NotContain("1 target(s) can move to v1.2.3");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

        [Fact]
        public void OnlineInstaller_TuiStartup_ShouldShowUpdateBannerForManagedCustomVisualStudioRegistrationWithoutState()
        {
                var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
                try
                {
                        var appData = Path.Combine(tempRoot, "AppData", "Roaming");
                        var localAppData = Path.Combine(tempRoot, "AppData", "Local");
                        var userProfile = Path.Combine(tempRoot, "UserProfile");
                        var installRoot = Path.Combine(tempRoot, "install-root");
                        var installBase = Path.Combine(installRoot, "x64");
                        var executablePath = Path.Combine(installBase, "current", "bin", "wpf-devtools-x64.exe");
                        var customVisualStudioConfigPath = Path.Combine(tempRoot, "custom", "visual-studio", ".mcp.json");

                        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
                        Directory.CreateDirectory(Path.GetDirectoryName(customVisualStudioConfigPath)!);
                        Directory.CreateDirectory(appData);
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
                                    "executable": "__EXECUTABLE__",
                                    "managedRegistrationTargets": {
                                        "visual-studio": "__VISUAL_STUDIO_CONFIG__"
                                    }
                                }
                                """
                                .Replace("__INSTALL_ROOT__", installRoot.Replace("\\", "\\\\"))
                                .Replace("__INSTALL_DIR__", Path.Combine(installBase, "current").Replace("\\", "\\\\"))
                                .Replace("__EXECUTABLE__", executablePath.Replace("\\", "\\\\"))
                                .Replace("__VISUAL_STUDIO_CONFIG__", customVisualStudioConfigPath.Replace("\\", "\\\\")));
                        File.WriteAllText(
                                customVisualStudioConfigPath,
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
                                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape||Enter'",
                                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other -InstallRoot '" + installRoot.Replace("'", "''") + "'"
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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape||Enter'",
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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape||Enter'",
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
    public void OnlineInstaller_InstallScreen_ShouldDisplayCurrentInstallLocationContext()
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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Enter||Escape||Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='100'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='30'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other -InstallRoot '" + installRoot.Replace("'", "''") + "'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Where would you like to install?");
            result.Stdout.Should().Contain("Install location: " + installRoot);
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
            var expectedExecutable = Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe");

            File.WriteAllText(
                Path.Combine(fakeBin, "codex.ps1"),
                "param([Parameter(ValueFromRemainingArguments=$true)][string[]]$args)" + Environment.NewLine +
                "Add-Content -Path '" + codexLog.Replace("'", "''") + "' -Value ($args -join ' ')" + Environment.NewLine +
                "if (($args[0] -eq 'mcp') -and ($args[1] -eq 'list')) { Write-Output 'wpf-devtools " + expectedExecutable.Replace("'", "''") + "'; exit 0 }" + Environment.NewLine +
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
                    ["WPFDEVTOOLS_INSTALLER_VERIFICATION_TIMEOUT_SEC"] = "5"
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
}
