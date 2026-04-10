using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerCursorClientTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeclareCursorClientSupportAndCodexCliLabel()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("'cursor'");
        content.Should().Contain(".cursor\\mcp.json");
        content.Should().Contain("Codex/Codex CLI");
    }

    [Fact]
    public void OnlineInstaller_InstallScreen_ShouldRenderCursorAndCodexCliLabels()
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

            var result = RunInteractiveInstaller(tempRoot, appData, localAppData, userProfile, new[]
            {
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Enter||Escape||Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'"
            });

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Where would you like to install?");
            result.Stdout.Should().Contain("Cursor");
            result.Stdout.Should().Contain("Codex/Codex CLI");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldCreateCursorRegistrationArtifactsForGlobalAndProjectModes()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
                });

            result.ExitCode.Should().Be(0, result.Stderr);

            var registrationDir = Path.Combine(installRoot, "x64", "client-registration");
            var globalCursorArtifact = Path.Combine(registrationDir, "cursor.global.json");
            var projectCursorArtifact = Path.Combine(registrationDir, "cursor.project.json");

            File.Exists(globalCursorArtifact).Should().BeTrue();
            File.Exists(projectCursorArtifact).Should().BeTrue();
            File.ReadAllText(globalCursorArtifact).Should().Contain("wpf-devtools").And.Contain("\"command\"");
            File.ReadAllText(projectCursorArtifact).Should().Contain("wpf-devtools").And.Contain("\"command\"");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldPersistCursorGlobalAndProjectRegistrationsSeparately()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var globalConfigPath = Path.Combine(userProfile, ".cursor", "mcp.json");
            var projectRoot = Path.Combine(tempRoot, "CursorProject");

            var globalInstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", globalConfigPath, "-NonInteractive", "-Force", "-OutputJson"],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile
                });
            globalInstall.ExitCode.Should().Be(0, globalInstall.Stderr);

            var projectInstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "project", "-CursorProjectRoot", projectRoot, "-NonInteractive", "-Force", "-OutputJson"],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile
                });
            projectInstall.ExitCode.Should().Be(0, projectInstall.Stderr);

            using var installJson = System.Text.Json.JsonDocument.Parse(projectInstall.Stdout);
            var statePath = installJson.RootElement.GetProperty("statePath").GetString();
            statePath.Should().NotBeNullOrWhiteSpace();
            using var stateJson = System.Text.Json.JsonDocument.Parse(File.ReadAllText(statePath!));
            var registrations = stateJson.RootElement.GetProperty("registrations");

            registrations.TryGetProperty("cursor-global", out var globalRegistration).Should().BeTrue();
            registrations.TryGetProperty("cursor-project", out var projectRegistration).Should().BeTrue();
            globalRegistration.GetProperty("target").GetString().Should().Be(globalConfigPath);
            projectRegistration.GetProperty("target").GetString().Should().Be(Path.Combine(projectRoot, ".cursor", "mcp.json"));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldUninstallCursorGlobalWithoutFailingWhenProjectScopeRemains()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var globalConfigPath = Path.Combine(userProfile, ".cursor", "mcp.json");
            var projectRoot = Path.Combine(tempRoot, "CursorProject");
            var projectConfigPath = Path.Combine(projectRoot, ".cursor", "mcp.json");

            foreach (var installArguments in new[]
            {
                new[] { "-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", globalConfigPath, "-NonInteractive", "-Force", "-OutputJson" },
                new[] { "-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "project", "-CursorProjectRoot", projectRoot, "-NonInteractive", "-Force", "-OutputJson" }
            })
            {
                var installResult = ReleaseScriptTestHarness.RunPowerShellScript(
                    ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                    installArguments,
                    new Dictionary<string, string?>
                    {
                        ["APPDATA"] = appData,
                        ["LOCALAPPDATA"] = localAppData,
                        ["USERPROFILE"] = userProfile
                    });
                installResult.ExitCode.Should().Be(0, installResult.Stderr);
            }

            var uninstallResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-Action", "uninstall", "-InstallRoot", installRoot, "-Architecture", "x64", "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", globalConfigPath, "-NonInteractive", "-OutputJson"],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile
                });

            uninstallResult.ExitCode.Should().Be(0, uninstallResult.Stderr);
            File.ReadAllText(globalConfigPath).Should().NotContain("wpf-devtools");
            File.ReadAllText(projectConfigPath).Should().Contain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_UninstallScreen_ShouldListCursorGlobalAndProjectScopesSeparately()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var globalConfigPath = Path.Combine(userProfile, ".cursor", "mcp.json");
            var projectRoot = Path.Combine(tempRoot, "CursorProject");

            foreach (var installArguments in new[]
            {
                new[] { "-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", globalConfigPath, "-NonInteractive", "-Force", "-OutputJson" },
                new[] { "-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "project", "-CursorProjectRoot", projectRoot, "-NonInteractive", "-Force", "-OutputJson" }
            })
            {
                var installResult = ReleaseScriptTestHarness.RunPowerShellScript(
                    ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                    installArguments,
                    new Dictionary<string, string?>
                    {
                        ["APPDATA"] = appData,
                        ["LOCALAPPDATA"] = localAppData,
                        ["USERPROFILE"] = userProfile
                    });
                installResult.ExitCode.Should().Be(0, installResult.Stderr);
            }

            var result = RunInteractiveInstaller(tempRoot, appData, localAppData, userProfile, new[]
            {
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='DownArrow||Enter||Escape||Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'"
            });

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Cursor (Global)");
            result.Stdout.Should().Contain("Cursor (Project)");
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
        string userProfile,
        IEnumerable<string> extraCommands)
    {
        var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
        var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        var commandParts = new List<string>
        {
            "$env:APPDATA='" + appData.Replace("'", "''") + "'",
            "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
            "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
            "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
            "Set-Location '" + tempRoot.Replace("'", "''") + "'"
        };

        commandParts.AddRange(extraCommands);
        commandParts.Add(
            "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other");

        return ReleaseScriptTestHarness.RunPowerShellCommand(string.Join(" ; ", commandParts));
    }
}
