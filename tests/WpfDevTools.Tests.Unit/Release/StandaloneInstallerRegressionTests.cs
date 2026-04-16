using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class StandaloneInstallerRegressionTests
{
    public static TheoryData<string> RemovalActions => new()
    {
        "uninstall",
        "full-uninstall"
    };

    [Theory]
    [MemberData(nameof(RemovalActions))]
    public void StandaloneOnlineInstaller_NonInteractiveRemovalModes_ShouldNotRequireHelperRuntime(string action)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var installWorkingRoot = Path.Combine(tempRoot, "working-install");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");
            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-WorkingRoot", installWorkingRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);

            var standaloneRoot = Path.Combine(tempRoot, "standalone");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", action,
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            using var json = JsonDocument.Parse(removal.Stdout);
            json.RootElement.GetProperty("action").GetString().Should().Be(action);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [MemberData(nameof(RemovalActions))]
    public void StandaloneOnlineInstaller_NonInteractiveRemovalModes_ShouldNotRequireInstalledHelperModules(string action)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");
            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);

            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var removalWorkingRoot = Path.Combine(tempRoot, "working-removal");
            var standaloneRoot = Path.Combine(tempRoot, "standalone-no-helpers");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            var wrapperPath = Path.Combine(standaloneRoot, "invoke-removal.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);
            File.WriteAllText(
                wrapperPath,
                string.Join(Environment.NewLine,
                [
                    "Set-Location '" + standaloneRoot.Replace("'", "''") + "'",
                    "& '" + standaloneScriptPath.Replace("'", "''") + "' " +
                    "-Action " + action + " " +
                    "-Architecture x64 " +
                    "-InstallRoot '" + installRoot.Replace("'", "''") + "' " +
                    "-WorkingRoot '" + removalWorkingRoot.Replace("'", "''") + "' " +
                    "-Client visual-studio " +
                    "-VisualStudioConfigPath '" + visualStudioConfigPath.Replace("'", "''") + "' " +
                    "-NonInteractive -Force -OutputJson"
                ]));

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                wrapperPath,
                [],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            using var json = JsonDocument.Parse(removal.Stdout);
            json.RootElement.GetProperty("action").GetString().Should().Be(action);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [MemberData(nameof(RemovalActions))]
    public void StandaloneOnlineInstaller_ExecutedOutsideRepo_NonInteractiveRemovalModes_ShouldNotRequireHelperRuntime(string action)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");
            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-external");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            var wrapperPath = Path.Combine(standaloneRoot, "invoke-removal.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);
            File.WriteAllText(
                wrapperPath,
                string.Join(Environment.NewLine,
                [
                    "Set-Location '" + standaloneRoot.Replace("'", "''") + "'",
                    "& '" + standaloneScriptPath.Replace("'", "''") + "' " +
                    "-Action " + action + " " +
                    "-Architecture x64 " +
                    "-InstallRoot '" + installRoot.Replace("'", "''") + "' " +
                    "-Client visual-studio " +
                    "-VisualStudioConfigPath '" + visualStudioConfigPath.Replace("'", "''") + "' " +
                    "-NonInteractive -Force -OutputJson"
                ]));

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                wrapperPath,
                [],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            using var json = JsonDocument.Parse(removal.Stdout);
            json.RootElement.GetProperty("action").GetString().Should().Be(action);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneOnlineInstaller_FullUninstall_ShouldRemoveJsonRegistrationsWhenHelperModulesAreUnavailable()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");
            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);

            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-full-uninstall");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            File.ReadAllText(visualStudioConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [InlineData("uninstall", "vscode")]
    [InlineData("uninstall", "visual-studio")]
    [InlineData("uninstall", "cursor-global")]
    [InlineData("uninstall", "cursor-project")]
    [InlineData("full-uninstall", "vscode")]
    [InlineData("full-uninstall", "visual-studio")]
    [InlineData("full-uninstall", "cursor-global")]
    [InlineData("full-uninstall", "cursor-project")]
    public void StandaloneOnlineInstaller_ManagedJsonRemovalModes_ShouldFailCleanlyWhenConfigIsMalformed(string action, string client)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var scenario = CreateMalformedJsonClientScenario(tempRoot, archivePath, installRoot, client);

            var install = RunRepoInstaller(tempRoot, scenario.InstallArguments);
            install.ExitCode.Should().Be(0, install.Stderr);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-malformed-json");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            const string malformedJson = "{ not valid json";
            File.WriteAllText(scenario.ConfigPath, malformedJson);

            var uninstall = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                action == "full-uninstall" ? scenario.FullUninstallArguments : scenario.UninstallArguments,
                CreateStandaloneEnvironment(tempRoot));

            uninstall.ExitCode.Should().NotBe(0);
            uninstall.Stderr.Should().Contain("Failed to parse JSON config file");
            File.ReadAllText(scenario.ConfigPath).Should().Be(malformedJson);

            using var stateDocument = JsonDocument.Parse(File.ReadAllText(statePath));
            stateDocument.RootElement.GetProperty("registrations").TryGetProperty(scenario.StateKey, out _).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneOnlineInstaller_Uninstall_ShouldRemoveCustomJsonRegistrationWithoutRequiringOverrideAgain()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var customVisualStudioConfigPath = Path.Combine(tempRoot, "custom", "visual-studio", ".mcp.json");
            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", customVisualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(customVisualStudioConfigPath).Should().Contain("wpf-devtools");

            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-custom-target");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            File.ReadAllText(customVisualStudioConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [MemberData(nameof(RemovalActions))]
    public void StandaloneOnlineInstaller_CursorRemovalModes_ShouldRemoveCustomCursorRegistrationWithoutRequiringOverrideAgain(string action)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var customCursorConfigPath = Path.Combine(tempRoot, "custom", "cursor", "mcp.json");
            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "cursor",
                    "-CursorMode", "global",
                    "-CursorConfigPath", customCursorConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(customCursorConfigPath).Should().Contain("wpf-devtools");

            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-cursor-custom-target");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", action,
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "cursor",
                    "-CursorMode", "global",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            File.ReadAllText(customCursorConfigPath).Should().NotContain("wpf-devtools");

            if (action == "full-uninstall")
            {
                Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            }
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneOnlineInstaller_Uninstall_ShouldRecoverCustomJsonRegistrationWhenStateFileIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var customVisualStudioConfigPath = Path.Combine(tempRoot, "custom", "visual-studio", ".mcp.json");
            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", customVisualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(customVisualStudioConfigPath).Should().Contain("wpf-devtools");

            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            File.Delete(statePath);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-custom-target-missing-state");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            File.ReadAllText(customVisualStudioConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Theory]
    [MemberData(nameof(RemovalActions))]
    public void StandaloneOnlineInstaller_CursorRemovalModes_ShouldRecoverCustomCursorRegistrationWhenStateFileIsMissing(string action)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var customCursorConfigPath = Path.Combine(tempRoot, "custom", "cursor", "mcp.json");
            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "cursor",
                    "-CursorMode", "global",
                    "-CursorConfigPath", customCursorConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(customCursorConfigPath).Should().Contain("wpf-devtools");

            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            File.Delete(statePath);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-cursor-custom-target-missing-state");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", action,
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "cursor",
                    "-CursorMode", "global",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            File.ReadAllText(customCursorConfigPath).Should().NotContain("wpf-devtools");

            if (action == "full-uninstall")
            {
                Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            }
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneOnlineInstaller_FullUninstall_ShouldRecoverViaLiveRegistrationDiscoveryWhenStateFileIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");
            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);

            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            File.Delete(statePath);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-full-uninstall-missing-state");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            File.ReadAllText(visualStudioConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneOnlineInstaller_Uninstall_ShouldRollbackArtifactRemovalWhenStateSaveFails()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);

            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var artifactPath = Path.Combine(installRoot, "x64", "client-registration", "other.mcpServers.json");
            File.Exists(artifactPath).Should().BeTrue();

            var standaloneRoot = Path.Combine(tempRoot, "standalone-artifact-rollback");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot, new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_FAIL_SAVE_STANDALONE_STATE"] = "1"
                }));

            removal.ExitCode.Should().NotBe(0);
            File.Exists(artifactPath).Should().BeTrue("artifact-only uninstall should roll back if standalone state persistence fails");
            File.ReadAllText(artifactPath).Should().Contain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneOnlineInstaller_FullUninstall_ShouldRollbackRegistrationsAndInstallRootWhenStateSaveFails()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");
            var hostileTargetPath = Path.Combine(tempRoot, "hostile", "unrelated.json");
            Directory.CreateDirectory(Path.GetDirectoryName(hostileTargetPath)!);
            File.WriteAllText(hostileTargetPath, "{\"keep\":true}");
            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            using (var stateDocument = JsonDocument.Parse(File.ReadAllText(statePath)))
            {
                var root = stateDocument.RootElement;
                var registrations = root.GetProperty("registrations");
                var visualStudio = registrations.GetProperty("visual-studio");

                var updatedState = new
                {
                    lastInstallRoot = root.GetProperty("lastInstallRoot").GetString(),
                    architectures = JsonSerializer.Deserialize<object>(root.GetProperty("architectures").GetRawText()),
                    registrations = new Dictionary<string, object?>
                    {
                        ["visual-studio"] = new
                        {
                            architecture = visualStudio.GetProperty("architecture").GetString(),
                            installRoot = visualStudio.GetProperty("installRoot").GetString(),
                            mode = visualStudio.GetProperty("mode").GetString(),
                            target = hostileTargetPath,
                            resolvedVersion = visualStudio.GetProperty("resolvedVersion").GetString(),
                            installedExecutable = visualStudio.GetProperty("installedExecutable").GetString(),
                            lastVerifiedUtc = visualStudio.GetProperty("lastVerifiedUtc").GetString()
                        }
                    }
                };

                File.WriteAllText(statePath, JsonSerializer.Serialize(updatedState));
            }

            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-full-uninstall-rollback");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot, new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_FAIL_SAVE_STANDALONE_STATE"] = "1"
                }));

            removal.ExitCode.Should().NotBe(0);
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeTrue(
                "standalone full-uninstall should restore the installer-owned install root when state persistence fails");
            File.ReadAllText(visualStudioConfigPath).Should().Contain("wpf-devtools",
                "standalone full-uninstall should restore the live JSON registration when state persistence fails");
            File.ReadAllText(hostileTargetPath).Should().Be("{\"keep\":true}");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneOnlineInstaller_FullUninstall_ShouldRemoveLiveJsonRegistrationWhenStateTargetIsStale()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var liveVisualStudioConfigPath = Path.Combine(tempRoot, "UserProfile", ".mcp.json");
            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(liveVisualStudioConfigPath).Should().Contain("wpf-devtools");

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            var staleTargetPath = Path.Combine(tempRoot, "stale", ".mcp.json");
            using (var stateDocument = JsonDocument.Parse(File.ReadAllText(statePath)))
            {
                var root = stateDocument.RootElement;
                var registrations = root.GetProperty("registrations");
                var visualStudio = registrations.GetProperty("visual-studio");

                var updatedState = new
                {
                    lastInstallRoot = root.GetProperty("lastInstallRoot").GetString(),
                    architectures = JsonSerializer.Deserialize<object>(root.GetProperty("architectures").GetRawText()),
                    registrations = new Dictionary<string, object?>
                    {
                        ["visual-studio"] = new
                        {
                            architecture = visualStudio.GetProperty("architecture").GetString(),
                            installRoot = visualStudio.GetProperty("installRoot").GetString(),
                            mode = visualStudio.GetProperty("mode").GetString(),
                            target = staleTargetPath,
                            resolvedVersion = visualStudio.GetProperty("resolvedVersion").GetString(),
                            installedExecutable = visualStudio.GetProperty("installedExecutable").GetString(),
                            lastVerifiedUtc = visualStudio.GetProperty("lastVerifiedUtc").GetString()
                        }
                    }
                };

                File.WriteAllText(statePath, JsonSerializer.Serialize(updatedState));
            }

            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-stale-target");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            File.ReadAllText(liveVisualStudioConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneOnlineInstaller_FullUninstall_ShouldIgnoreHostileStateTargetForVisualStudioConfig()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var liveVisualStudioConfigPath = Path.Combine(tempRoot, "UserProfile", ".mcp.json");
            var hostileTargetPath = Path.Combine(tempRoot, "hostile", "unrelated.json");
            Directory.CreateDirectory(Path.GetDirectoryName(hostileTargetPath)!);
            File.WriteAllText(hostileTargetPath, "{\"keep\":true}");

            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);
            File.ReadAllText(liveVisualStudioConfigPath).Should().Contain("wpf-devtools");

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            using (var stateDocument = JsonDocument.Parse(File.ReadAllText(statePath)))
            {
                var root = stateDocument.RootElement;
                var registrations = root.GetProperty("registrations");
                var visualStudio = registrations.GetProperty("visual-studio");

                var updatedState = new
                {
                    lastInstallRoot = root.GetProperty("lastInstallRoot").GetString(),
                    architectures = JsonSerializer.Deserialize<object>(root.GetProperty("architectures").GetRawText()),
                    registrations = new Dictionary<string, object?>
                    {
                        ["visual-studio"] = new
                        {
                            architecture = visualStudio.GetProperty("architecture").GetString(),
                            installRoot = visualStudio.GetProperty("installRoot").GetString(),
                            mode = visualStudio.GetProperty("mode").GetString(),
                            target = hostileTargetPath,
                            resolvedVersion = visualStudio.GetProperty("resolvedVersion").GetString(),
                            installedExecutable = visualStudio.GetProperty("installedExecutable").GetString(),
                            lastVerifiedUtc = visualStudio.GetProperty("lastVerifiedUtc").GetString()
                        }
                    }
                };

                File.WriteAllText(statePath, JsonSerializer.Serialize(updatedState));
            }

            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-hostile-visual-studio-target");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            File.ReadAllText(liveVisualStudioConfigPath).Should().NotContain("wpf-devtools");
            File.ReadAllText(hostileTargetPath).Should().Be("{\"keep\":true}");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneOnlineInstaller_Uninstall_ShouldIgnoreHostileStateTargetForOtherClientArtifact()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var hostileTargetPath = Path.Combine(tempRoot, "hostile", "unrelated.json");
            Directory.CreateDirectory(Path.GetDirectoryName(hostileTargetPath)!);
            File.WriteAllText(hostileTargetPath, "{\"keep\":true}");

            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);

            var artifactPath = Path.Combine(installRoot, "x64", "client-registration", "other.mcpServers.json");
            File.Exists(artifactPath).Should().BeTrue();

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            using (var stateDocument = JsonDocument.Parse(File.ReadAllText(statePath)))
            {
                var root = stateDocument.RootElement;
                var registrations = root.GetProperty("registrations");
                var other = registrations.GetProperty("other");

                var updatedState = new
                {
                    lastInstallRoot = root.GetProperty("lastInstallRoot").GetString(),
                    architectures = JsonSerializer.Deserialize<object>(root.GetProperty("architectures").GetRawText()),
                    registrations = new Dictionary<string, object?>
                    {
                        ["other"] = new
                        {
                            architecture = other.GetProperty("architecture").GetString(),
                            installRoot = other.GetProperty("installRoot").GetString(),
                            mode = other.GetProperty("mode").GetString(),
                            target = hostileTargetPath,
                            resolvedVersion = other.GetProperty("resolvedVersion").GetString(),
                            installedExecutable = other.GetProperty("installedExecutable").GetString(),
                            lastVerifiedUtc = other.GetProperty("lastVerifiedUtc").GetString()
                        }
                    }
                };

                File.WriteAllText(statePath, JsonSerializer.Serialize(updatedState));
            }

            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-hostile-other-target");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            File.Exists(artifactPath).Should().BeFalse();
            File.ReadAllText(hostileTargetPath).Should().Be("{\"keep\":true}");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneOnlineInstaller_Uninstall_ShouldRemoveOtherArtifactWithoutExplicitInstallRootWhenManifestIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");

            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            install.ExitCode.Should().Be(0, install.Stderr);

            var artifactPath = Path.Combine(installRoot, "x64", "client-registration", "other.mcpServers.json");
            var manifestPath = Path.Combine(installRoot, "x64", "install-manifest.json");
            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            File.Exists(artifactPath).Should().BeTrue();
            File.Delete(manifestPath);
            ReleaseScriptTestHarness.DeleteDirectory(installedHelperRoot);

            var standaloneRoot = Path.Combine(tempRoot, "standalone-missing-manifest-other-target");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScriptPath,
                overwrite: true);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneScriptPath,
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-Client", "other",
                    "-NonInteractive",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            using var removalJson = JsonDocument.Parse(removal.Stdout);
            var registration = removalJson.RootElement.GetProperty("registrations").EnumerateArray().Single();
            registration.GetProperty("target").GetString().Should().Be(artifactPath);
            registration.GetProperty("applied").GetBoolean().Should().BeTrue();
            File.Exists(artifactPath).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunRepoInstaller(
        string tempRoot,
        IReadOnlyList<string> arguments)
        => ReleaseScriptTestHarness.RunPowerShellScript(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
            arguments,
            CreateStandaloneEnvironment(tempRoot));

    private static IReadOnlyDictionary<string, string?> CreateStandaloneEnvironment(
        string tempRoot,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var environment = new Dictionary<string, string?>
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
            ["WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI"] = "http://127.0.0.1:1/installer",
            ["WPFDEVTOOLS_INSTALLER_HELPER_TIMEOUT_SEC"] = "1",
            ["WPFDEVTOOLS_INSTALLER_HELPER_BOOTSTRAP_TIMEOUT_SEC"] = "3"
        };

        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                environment[pair.Key] = pair.Value;
            }
        }

        return environment;
    }

    private static (string StateKey, string ConfigPath, string[] InstallArguments, string[] UninstallArguments, string[] FullUninstallArguments) CreateMalformedJsonClientScenario(
        string tempRoot,
        string archivePath,
        string installRoot,
        string client)
        => client switch
        {
            "vscode" => (
                "vscode",
                Path.Combine(tempRoot, "config", "Code", "User", "mcp.json"),
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-VsCodeConfigPath", Path.Combine(tempRoot, "config", "Code", "User", "mcp.json"),
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-VsCodeConfigPath", Path.Combine(tempRoot, "config", "Code", "User", "mcp.json"),
                    "-NonInteractive",
                    "-OutputJson"
                ],
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-VsCodeConfigPath", Path.Combine(tempRoot, "config", "Code", "User", "mcp.json"),
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]),
            "visual-studio" => (
                "visual-studio",
                Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json"),
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json"),
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json"),
                    "-NonInteractive",
                    "-OutputJson"
                ],
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-VisualStudioConfigPath", Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json"),
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]),
            "cursor-global" => (
                "cursor-global",
                Path.Combine(tempRoot, "config", "cursor", "global", "mcp.json"),
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "cursor",
                    "-CursorMode", "global",
                    "-CursorConfigPath", Path.Combine(tempRoot, "config", "cursor", "global", "mcp.json"),
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "cursor",
                    "-CursorMode", "global",
                    "-CursorConfigPath", Path.Combine(tempRoot, "config", "cursor", "global", "mcp.json"),
                    "-NonInteractive",
                    "-OutputJson"
                ],
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "cursor",
                    "-CursorMode", "global",
                    "-CursorConfigPath", Path.Combine(tempRoot, "config", "cursor", "global", "mcp.json"),
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]),
            "cursor-project" => (
                "cursor-project",
                Path.Combine(tempRoot, "CursorProject", ".cursor", "mcp.json"),
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "cursor",
                    "-CursorMode", "project",
                    "-CursorProjectRoot", Path.Combine(tempRoot, "CursorProject"),
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "cursor",
                    "-CursorMode", "project",
                    "-CursorProjectRoot", Path.Combine(tempRoot, "CursorProject"),
                    "-NonInteractive",
                    "-OutputJson"
                ],
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "cursor",
                    "-CursorMode", "project",
                    "-CursorProjectRoot", Path.Combine(tempRoot, "CursorProject"),
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]),
            _ => throw new ArgumentOutOfRangeException(nameof(client), client, null)
        };
}
