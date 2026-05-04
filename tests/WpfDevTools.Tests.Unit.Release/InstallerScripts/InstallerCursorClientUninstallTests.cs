using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerCursorClientUninstallTests
{
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
                    CreateInstallerEnvironment(appData, localAppData, userProfile));
                installResult.ExitCode.Should().Be(0, installResult.Stderr);
            }

            var uninstallResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                removedScope == "global"
                    ? ["-Action", "uninstall", "-InstallRoot", installRoot, "-Architecture", "x64", "-Client", "cursor", "-CursorMode", "global", "-CursorConfigPath", globalConfigPath, "-NonInteractive", "-OutputJson"]
                    : ["-Action", "uninstall", "-InstallRoot", installRoot, "-Architecture", "x64", "-Client", "cursor", "-CursorMode", "project", "-CursorProjectRoot", projectRoot, "-NonInteractive", "-OutputJson"],
                CreateInstallerEnvironment(appData, localAppData, userProfile));

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
            var environment = CreateInstallerEnvironment(appData, localAppData, userProfile);
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
            var environment = CreateInstallerEnvironment(appData, localAppData, userProfile);
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
            var environment = CreateInstallerEnvironment(appData, localAppData, userProfile);
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

            var environment = CreateInstallerEnvironment(appData, localAppData, userProfile);

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

    private static Dictionary<string, string?> CreateInstallerEnvironment(
        string appData,
        string localAppData,
        string userProfile)
        => new()
        {
            ["APPDATA"] = appData,
            ["LOCALAPPDATA"] = localAppData,
            ["USERPROFILE"] = userProfile
        };
}