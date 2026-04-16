using FluentAssertions;
using System.Text.Json;
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

    [Theory]
    [InlineData("global")]
    [InlineData("project")]
    public void OnlineInstaller_ShouldCreateCursorRegistrationArtifactsForInstalledCursorModes(string cursorMode)
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

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                cursorMode == "global"
                    ? ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", globalConfigPath, "-NonInteractive", "-Force", "-OutputJson"]
                    : ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "project", "-CursorProjectRoot", projectRoot, "-NonInteractive", "-Force", "-OutputJson"],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile
                });

            result.ExitCode.Should().Be(0, result.Stderr);

            var registrationDir = Path.Combine(installRoot, "x64", "client-registration");
            var globalCursorArtifact = Path.Combine(registrationDir, "cursor.global.json");
            var projectCursorArtifact = Path.Combine(registrationDir, "cursor.project.json");

            File.Exists(globalCursorArtifact).Should().BeTrue();
            File.Exists(projectCursorArtifact).Should().BeTrue();
            File.ReadAllText(globalCursorArtifact).Should().Contain("wpf-devtools").And.Contain("\"command\"");
            File.ReadAllText(projectCursorArtifact).Should().Contain("wpf-devtools").And.Contain("\"command\"");

            using var installJson = JsonDocument.Parse(result.Stdout);
            var registrations = installJson.RootElement.GetProperty("registrations").EnumerateArray().ToArray();
            registrations.Should().ContainSingle();
            registrations[0].GetProperty("target").GetString().Should().Be(
                cursorMode == "global"
                    ? globalConfigPath
                    : Path.Combine(projectRoot, ".cursor", "mcp.json"));
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
            var globalInstallRoot = Path.Combine(tempRoot, "install-root-global");
            var projectInstallRoot = Path.Combine(tempRoot, "install-root-project");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var globalConfigPath = Path.Combine(userProfile, ".cursor", "mcp.json");
            var projectRoot = Path.Combine(tempRoot, "CursorProject");

            var globalInstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-PackageArchivePath", archivePath, "-InstallRoot", globalInstallRoot, "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", globalConfigPath, "-NonInteractive", "-Force", "-OutputJson"],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile
                });
            globalInstall.ExitCode.Should().Be(0, globalInstall.Stderr);

            var projectInstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-PackageArchivePath", archivePath, "-InstallRoot", projectInstallRoot, "-Client", "cursor", "-CursorMode", "project", "-CursorProjectRoot", projectRoot, "-NonInteractive", "-Force", "-OutputJson"],
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

    [Theory]
    [InlineData("global")]
    [InlineData("project")]
    public void OnlineInstaller_ShouldUninstallOnlySelectedCursorScopeAndKeepInstallerStateAccurate(string removedScope)
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
            JsonRegistrationTestAssertions.SeedRegistrationFile(globalConfigPath, "mcpServers", "existing", "global-existing.exe");
            JsonRegistrationTestAssertions.SeedRegistrationFile(projectConfigPath, "mcpServers", "existing", "project-existing.exe");

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
                    removedScope == "global"
                        ? ["-Action", "uninstall", "-InstallRoot", installRoot, "-Architecture", "x64", "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", globalConfigPath, "-NonInteractive", "-OutputJson"]
                        : ["-Action", "uninstall", "-InstallRoot", installRoot, "-Architecture", "x64", "-Client", "cursor", "-CursorMode", "project", "-CursorProjectRoot", projectRoot, "-NonInteractive", "-OutputJson"],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile
                });

            uninstallResult.ExitCode.Should().Be(0, uninstallResult.Stderr);

                if (removedScope == "global")
                {
                    JsonRegistrationTestAssertions.AssertRegistrationAbsent(globalConfigPath, "mcpServers", "wpf-devtools");
                    JsonRegistrationTestAssertions.AssertRegistrationCommand(globalConfigPath, "mcpServers", "existing", "global-existing.exe");
                    JsonRegistrationTestAssertions.AssertRegistrationPresent(projectConfigPath, "mcpServers", "wpf-devtools");
                    JsonRegistrationTestAssertions.AssertRegistrationCommand(projectConfigPath, "mcpServers", "existing", "project-existing.exe");
                }
                else
                {
                    JsonRegistrationTestAssertions.AssertRegistrationAbsent(projectConfigPath, "mcpServers", "wpf-devtools");
                    JsonRegistrationTestAssertions.AssertRegistrationCommand(projectConfigPath, "mcpServers", "existing", "project-existing.exe");
                    JsonRegistrationTestAssertions.AssertRegistrationPresent(globalConfigPath, "mcpServers", "wpf-devtools");
                    JsonRegistrationTestAssertions.AssertRegistrationCommand(globalConfigPath, "mcpServers", "existing", "global-existing.exe");
                }

                var statePath = Path.Combine(appData, "WpfDevToolsMcp", "installer-state.json");
                using var stateJson = JsonDocument.Parse(File.ReadAllText(statePath));
                var registrations = stateJson.RootElement.GetProperty("registrations");
                registrations.TryGetProperty(removedScope == "global" ? "cursor-global" : "cursor-project", out _).Should().BeFalse();
                registrations.TryGetProperty(removedScope == "global" ? "cursor-project" : "cursor-global", out var remainingRegistration).Should().BeTrue();
                remainingRegistration.GetProperty("target").GetString().Should().Be(
                    removedScope == "global"
                        ? projectConfigPath
                        : globalConfigPath);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldRemoveCustomCursorGlobalRegistrationWithoutRequiringOverrideAgain()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var customConfigPath = Path.Combine(tempRoot, "custom", "cursor", "mcp.json");
            var environment = new Dictionary<string, string?>
            {
                ["APPDATA"] = appData,
                ["LOCALAPPDATA"] = localAppData,
                ["USERPROFILE"] = userProfile
            };
            JsonRegistrationTestAssertions.SeedRegistrationFile(customConfigPath, "mcpServers", "existing", "custom-existing.exe");

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", customConfigPath, "-NonInteractive", "-Force", "-OutputJson"],
                environment);
            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(customConfigPath).Should().Contain("wpf-devtools");

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-Action", "uninstall", "-InstallRoot", installRoot, "-Architecture", "x64", "-Client", "cursor", "-CursorMode", "global", "-NonInteractive", "-OutputJson"],
                environment);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            JsonRegistrationTestAssertions.AssertRegistrationAbsent(customConfigPath, "mcpServers", "wpf-devtools");
            JsonRegistrationTestAssertions.AssertRegistrationCommand(customConfigPath, "mcpServers", "existing", "custom-existing.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldNotRollbackCustomCursorGlobalRemovalWhenDefaultConfigStillContainsAnotherRegistration()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var customConfigPath = Path.Combine(tempRoot, "custom", "cursor", "mcp.json");
            var defaultConfigPath = Path.Combine(userProfile, ".cursor", "mcp.json");
            var environment = new Dictionary<string, string?>
            {
                ["APPDATA"] = appData,
                ["LOCALAPPDATA"] = localAppData,
                ["USERPROFILE"] = userProfile
            };
            JsonRegistrationTestAssertions.SeedRegistrationFile(customConfigPath, "mcpServers", "existing", "custom-existing.exe");

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", customConfigPath, "-NonInteractive", "-Force", "-OutputJson"],
                environment);
            install.ExitCode.Should().Be(0, install.Stderr);

            JsonRegistrationTestAssertions.SeedRegistrationFile(defaultConfigPath, "mcpServers", "wpf-devtools", "C:/external/wpf-devtools-x64.exe");

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-Action", "uninstall", "-InstallRoot", installRoot, "-Architecture", "x64", "-Client", "cursor", "-CursorMode", "global", "-NonInteractive", "-OutputJson"],
                environment);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            JsonRegistrationTestAssertions.AssertRegistrationAbsent(customConfigPath, "mcpServers", "wpf-devtools");
            JsonRegistrationTestAssertions.AssertRegistrationCommand(customConfigPath, "mcpServers", "existing", "custom-existing.exe");
            JsonRegistrationTestAssertions.AssertRegistrationPresent(defaultConfigPath, "mcpServers", "wpf-devtools");
            JsonRegistrationTestAssertions.AssertRegistrationCommand(defaultConfigPath, "mcpServers", "wpf-devtools", "C:/external/wpf-devtools-x64.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldRecoverCustomCursorProjectRegistrationWhenStateFileIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var projectRoot = Path.Combine(tempRoot, "CustomCursorProject");
            var projectConfigPath = Path.Combine(projectRoot, ".cursor", "mcp.json");
            var environment = new Dictionary<string, string?>
            {
                ["APPDATA"] = appData,
                ["LOCALAPPDATA"] = localAppData,
                ["USERPROFILE"] = userProfile
            };
            JsonRegistrationTestAssertions.SeedRegistrationFile(projectConfigPath, "mcpServers", "existing", "project-existing.exe");

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "project", "-CursorProjectRoot", projectRoot, "-NonInteractive", "-Force", "-OutputJson"],
                environment);
            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(projectConfigPath).Should().Contain("wpf-devtools");

            var statePath = Path.Combine(appData, "WpfDevToolsMcp", "installer-state.json");
            File.Delete(statePath);

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-Action", "uninstall", "-InstallRoot", installRoot, "-Architecture", "x64", "-Client", "cursor", "-CursorMode", "project", "-NonInteractive", "-OutputJson"],
                environment);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            JsonRegistrationTestAssertions.AssertRegistrationAbsent(projectConfigPath, "mcpServers", "wpf-devtools");
            JsonRegistrationTestAssertions.AssertRegistrationCommand(projectConfigPath, "mcpServers", "existing", "project-existing.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [InlineData("global")]
    [InlineData("project")]
    public void OnlineInstaller_Uninstall_ShouldIgnoreHostileStateTargetForCursorRegistrations(string cursorMode)
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
            var hostileTargetPath = Path.Combine(tempRoot, "hostile", cursorMode + "-cursor.json");
            Directory.CreateDirectory(Path.GetDirectoryName(hostileTargetPath)!);
            File.WriteAllText(hostileTargetPath, "{\"keep\":true}");
            JsonRegistrationTestAssertions.SeedRegistrationFile(
                cursorMode == "global" ? globalConfigPath : projectConfigPath,
                "mcpServers",
                "existing",
                cursorMode == "global" ? "global-existing.exe" : "project-existing.exe");

            var installArguments = cursorMode == "global"
                ? new[] { "-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", globalConfigPath, "-NonInteractive", "-Force", "-OutputJson" }
                : new[] { "-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "cursor", "-CursorMode", "project", "-CursorProjectRoot", projectRoot, "-NonInteractive", "-Force", "-OutputJson" };

            var environment = new Dictionary<string, string?>
            {
                ["APPDATA"] = appData,
                ["LOCALAPPDATA"] = localAppData,
                ["USERPROFILE"] = userProfile
            };

            var installResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                installArguments,
                environment);
            installResult.ExitCode.Should().Be(0, installResult.Stderr);

            var statePath = Path.Combine(appData, "WpfDevToolsMcp", "installer-state.json");
            var stateKey = cursorMode == "global" ? "cursor-global" : "cursor-project";
            using (var stateDocument = JsonDocument.Parse(File.ReadAllText(statePath)))
            {
                var root = stateDocument.RootElement;
                var registrations = root.GetProperty("registrations");
                var registration = registrations.GetProperty(stateKey);

                var updatedState = new
                {
                    lastInstallRoot = root.GetProperty("lastInstallRoot").GetString(),
                    architectures = JsonSerializer.Deserialize<object>(root.GetProperty("architectures").GetRawText()),
                    registrations = new Dictionary<string, object?>
                    {
                        [stateKey] = new
                        {
                            architecture = registration.GetProperty("architecture").GetString(),
                            installRoot = registration.GetProperty("installRoot").GetString(),
                            mode = registration.GetProperty("mode").GetString(),
                            target = hostileTargetPath,
                            resolvedVersion = registration.GetProperty("resolvedVersion").GetString(),
                            installedExecutable = registration.GetProperty("installedExecutable").GetString(),
                            lastVerifiedUtc = registration.GetProperty("lastVerifiedUtc").GetString()
                        }
                    }
                };

                File.WriteAllText(statePath, JsonSerializer.Serialize(updatedState));
            }

            var uninstallArguments = cursorMode == "global"
                ? new[] { "-Action", "uninstall", "-InstallRoot", installRoot, "-Architecture", "x64", "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", globalConfigPath, "-NonInteractive", "-OutputJson" }
                : new[] { "-Action", "uninstall", "-InstallRoot", installRoot, "-Architecture", "x64", "-Client", "cursor", "-CursorMode", "project", "-CursorProjectRoot", projectRoot, "-NonInteractive", "-OutputJson" };

            var uninstallResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                uninstallArguments,
                environment);

            uninstallResult.ExitCode.Should().Be(0, uninstallResult.Stderr);
            File.ReadAllText(hostileTargetPath).Should().Be("{\"keep\":true}");

            var safeTargetPath = cursorMode == "global" ? globalConfigPath : projectConfigPath;
            JsonRegistrationTestAssertions.AssertRegistrationAbsent(safeTargetPath, "mcpServers", "wpf-devtools");
            JsonRegistrationTestAssertions.AssertRegistrationCommand(
                safeTargetPath,
                "mcpServers",
                "existing",
                cursorMode == "global" ? "global-existing.exe" : "project-existing.exe");
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
