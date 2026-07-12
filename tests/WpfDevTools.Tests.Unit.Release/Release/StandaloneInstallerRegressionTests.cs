using System.Text.Json;
using FluentAssertions;
using Xunit;
using static WpfDevTools.Tests.Unit.Release.StandaloneInstallerRegressionTestSupport;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class StandaloneInstallerRegressionBootstrapTests
{
    [Theory]
    [MemberData(nameof(StandaloneInstallerRegressionTestSupport.RemovalActions), MemberType = typeof(StandaloneInstallerRegressionTestSupport))]
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
    [MemberData(nameof(StandaloneInstallerRegressionTestSupport.RemovalActions), MemberType = typeof(StandaloneInstallerRegressionTestSupport))]
    public void StandaloneOnlineInstaller_ExecutedOutsideRepo_RemovalModes_ShouldNotRequireAnyHelperRuntime(string action)
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
    [MemberData(nameof(StandaloneInstallerRegressionTestSupport.RemovalActions), MemberType = typeof(StandaloneInstallerRegressionTestSupport))]
    public void StandaloneOnlineInstaller_CodexRemovalModes_ShouldVerifyCliRegistrationWithoutHelperRuntime(string action)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeCliRoot = Path.Combine(tempRoot, "fake-cli");
            var markerPath = Path.Combine(tempRoot, "codex-registration.txt");
            Directory.CreateDirectory(fakeCliRoot);
            WriteFakeCli(fakeCliRoot, "codex", markerPath, "FAKE_CODEX_REGISTERED_PATH");

            var environmentOverrides = new Dictionary<string, string?>
            {
                ["FAKE_CODEX_REGISTERED_PATH"] = Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe"),
                ["PATH"] = fakeCliRoot + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
            };
            var installEnvironment = CreateStandaloneEnvironment(
                tempRoot,
                environmentOverrides);

            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "codex",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                installEnvironment);
            install.ExitCode.Should().Be(0, install.Stderr);
            File.Exists(markerPath).Should().BeTrue("the fake codex registration should be installed before removal");

            var standaloneRoot = Path.Combine(tempRoot, "standalone-codex-removal");
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
                    "-Client", "codex",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot, environmentOverrides));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            using var json = JsonDocument.Parse(removal.Stdout);
            json.RootElement.GetProperty("action").GetString().Should().Be(action);
            File.Exists(markerPath).Should().BeFalse("codex registration should be removed and verified");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneOnlineInstaller_CodexUninstallWithoutInstallRoot_ShouldHandleMissingCodexRegistration()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var defaultInstallRoot = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp");
            var fakeCliRoot = Path.Combine(tempRoot, "fake-cli");
            var claudeMarkerPath = Path.Combine(tempRoot, "claude-registration.txt");
            var codexMarkerPath = Path.Combine(tempRoot, "codex-registration.txt");
            Directory.CreateDirectory(fakeCliRoot);
            WriteFakeCli(fakeCliRoot, "claude", claudeMarkerPath, "FAKE_CLAUDE_REGISTERED_PATH");
            WriteFakeCli(fakeCliRoot, "codex", codexMarkerPath, "FAKE_CODEX_REGISTERED_PATH", supportsAdd: false);

            var environmentOverrides = new Dictionary<string, string?>
            {
                ["FAKE_CLAUDE_REGISTERED_PATH"] = Path.Combine(defaultInstallRoot, "x64", "current", "bin", "wpf-devtools-x64.exe"),
                ["FAKE_CODEX_REGISTERED_PATH"] = Path.Combine(defaultInstallRoot, "x64", "current", "bin", "wpf-devtools-x64.exe"),
                ["PATH"] = fakeCliRoot + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
            };

            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot, environmentOverrides));
            install.ExitCode.Should().Be(0, install.Stderr);
            File.Exists(claudeMarkerPath).Should().BeTrue("the default fake claude-code registration should be installed");
            File.Exists(codexMarkerPath).Should().BeFalse("codex should be absent before the no-op uninstall");

            var standaloneRoot = Path.Combine(tempRoot, "standalone-codex-default-root");
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
                    "-Client", "codex",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot, environmentOverrides));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            using var json = JsonDocument.Parse(removal.Stdout);
            json.RootElement.GetProperty("action").GetString().Should().Be("uninstall");
            File.Exists(claudeMarkerPath).Should().BeTrue("uninstalling codex should not remove the default claude-code registration");
            File.Exists(codexMarkerPath).Should().BeFalse("codex should remain absent after verified no-op cleanup");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void StandaloneOnlineInstaller_FullUninstall_ShouldAcceptCliListExitCodeWhenRegistrationIsAbsent()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var defaultInstallRoot = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp");
            var fakeCliRoot = Path.Combine(tempRoot, "fake-cli");
            var claudeMarkerPath = Path.Combine(tempRoot, "claude-registration.txt");
            Directory.CreateDirectory(fakeCliRoot);
            WriteFakeCli(
                fakeCliRoot,
                "claude",
                claudeMarkerPath,
                "FAKE_CLAUDE_REGISTERED_PATH",
                exitWithOneWhenAbsent: true);

            var environmentOverrides = new Dictionary<string, string?>
            {
                ["FAKE_CLAUDE_REGISTERED_PATH"] = Path.Combine(defaultInstallRoot, "x64", "current", "bin", "wpf-devtools-x64.exe"),
                ["PATH"] = fakeCliRoot + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
            };

            var install = RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot, environmentOverrides));
            install.ExitCode.Should().Be(0, install.Stderr);
            File.Exists(claudeMarkerPath).Should().BeTrue("the fake claude-code registration should be installed before full uninstall");

            var standaloneRoot = Path.Combine(tempRoot, "standalone-full-uninstall-cli");
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
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateStandaloneEnvironment(tempRoot, environmentOverrides));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            using var json = JsonDocument.Parse(removal.Stdout);
            json.RootElement.GetProperty("action").GetString().Should().Be("full-uninstall");
            File.Exists(claudeMarkerPath).Should().BeFalse("full-uninstall should remove the cli registration");
            Directory.Exists(Path.Combine(defaultInstallRoot, "x64")).Should().BeFalse("full-uninstall should remove the installed package base");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static void WriteFakeCli(
        string fakeCliRoot,
        string commandName,
        string markerPath,
        string registeredPathVariable,
        bool exitWithOneWhenAbsent = false,
        bool supportsAdd = true)
    {
        var lines = new List<string> { "@echo off" };
        if (supportsAdd)
        {
            lines.AddRange(
            [
                "if \"%1\"==\"mcp\" if \"%2\"==\"add\" (",
                "  >\"" + markerPath + "\" echo registered",
                "  exit /b 0",
                ")"
            ]);
        }

        lines.AddRange(
        [
            "if \"%1\"==\"mcp\" if \"%2\"==\"remove\" (",
            "  if exist \"" + markerPath + "\" del \"" + markerPath + "\"",
            "  exit /b 0",
            ")",
            "if \"%1\"==\"mcp\" if \"%2\"==\"list\" (",
            "  if exist \"" + markerPath + "\" (",
            "    echo wpf-devtools %" + registeredPathVariable + "%",
            "    exit /b 0",
            "  )",
            exitWithOneWhenAbsent ? "  exit /b 1" : "  exit /b 0",
            ")",
            "exit /b 1"
        ]);
        File.WriteAllText(Path.Combine(fakeCliRoot, commandName + ".cmd"), string.Join("\r\n", lines));
    }
}
