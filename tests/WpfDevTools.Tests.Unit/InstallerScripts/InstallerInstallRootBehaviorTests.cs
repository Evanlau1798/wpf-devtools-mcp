using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class InstallerInstallRootBehaviorTests
{
    [Fact]
    public void OnlineInstaller_HomeScreen_ShouldIgnoreStateOnlyArchitectureEvidenceWhenFilesAreMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var stateDir = Path.Combine(appData, "WpfDevToolsMcp");
            var staleRoot = Path.Combine(tempRoot, "stale-install-root");
            Directory.CreateDirectory(stateDir);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            File.WriteAllText(
                Path.Combine(stateDir, "installer-state.json"),
                $$"""
                {
                  "schemaVersion": 1,
                  "lastInstallRoot": "{{staleRoot.Replace("\\", "\\\\")}}",
                  "architectures": {
                    "x64": {
                      "version": "1.2.3",
                      "executable": "{{Path.Combine(staleRoot, "x64", "current", "bin", "wpf-devtools-x64.exe").Replace("\\", "\\\\")}}",
                      "installRoot": "{{staleRoot.Replace("\\", "\\\\")}}"
                    }
                  },
                  "registrations": {}
                }
                """);

            var result = RunInteractiveInstaller(tempRoot, appData, localAppData, userProfile);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("AppData");
            result.Stdout.Should().Contain("WpfDevToolsMcp");
            result.Stdout.Should().NotContain(staleRoot);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_HomeScreen_ShouldPreserveCustomRootWhenManifestAndExecutableStillExist()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var stateDir = Path.Combine(appData, "WpfDevToolsMcp");
            var liveRoot = Path.Combine(tempRoot, "live-install-root");
            var installBase = Path.Combine(liveRoot, "x64");
            var executablePath = Path.Combine(installBase, "current", "bin", "wpf-devtools-x64.exe");
            Directory.CreateDirectory(stateDir);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);

            File.WriteAllText(executablePath, "stub");
            File.WriteAllText(
                Path.Combine(installBase, "install-manifest.json"),
                $$"""
                {
                  "name": "wpf-devtools",
                  "architecture": "x64",
                  "version": "1.2.3",
                  "installRoot": "{{liveRoot.Replace("\\", "\\\\")}}",
                  "installDir": "{{Path.Combine(installBase, "current").Replace("\\", "\\\\")}}",
                  "executable": "{{executablePath.Replace("\\", "\\\\")}}"
                }
                """);
            File.WriteAllText(
                Path.Combine(stateDir, "installer-state.json"),
                $$"""
                {
                  "schemaVersion": 1,
                  "lastInstallRoot": "{{liveRoot.Replace("\\", "\\\\")}}",
                  "architectures": {},
                  "registrations": {}
                }
                """);

            var result = RunInteractiveInstaller(tempRoot, appData, localAppData, userProfile);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Install location");
            result.Stdout.Should().Contain("live-install-root");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunInteractiveInstaller(
        string tempRoot,
        string appData,
        string localAppData,
        string userProfile)
    {
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

        return ReleaseScriptTestHarness.RunPowerShellCommand(command);
    }
}
