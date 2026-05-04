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
    [MemberData(nameof(StandaloneInstallerRegressionTestSupport.RemovalActions), MemberType = typeof(StandaloneInstallerRegressionTestSupport))]
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
}
