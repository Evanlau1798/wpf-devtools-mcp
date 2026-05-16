using System.Text.Json;
using FluentAssertions;
using Xunit;
using static WpfDevTools.Tests.Unit.Release.InstallerScriptTestSupport;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerUninstallScriptTests
{
    [Fact]
    public void OnlineInstaller_Uninstall_ShouldRemoveClientRegistrationButKeepBinaryWhenAnotherClientStillUsesIt()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");

            RunInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-VsCodeConfigPath", vscodeConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]).ExitCode.Should().Be(0);

            RunInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-OutputJson"
                ]).ExitCode.Should().Be(0);

            var uninstall = RunInstaller(
                tempRoot,
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-VsCodeConfigPath", vscodeConfigPath,
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-OutputJson"
                ]);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            File.Exists(Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe")).Should().BeTrue();
            File.ReadAllText(vscodeConfigPath).Should().NotContain("wpf-devtools");
            File.ReadAllText(visualStudioConfigPath).Should().Contain("wpf-devtools");

            using var json = JsonDocument.Parse(uninstall.Stdout);
            json.RootElement.GetProperty("removedInstallation").GetBoolean().Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldKeepServerFilesWhenLastRegistrationIsRemoved()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");

            var install = RunInstaller(
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

            var uninstall = RunInstaller(
                tempRoot,
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-OutputJson"
                ]);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            File.Exists(Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe")).Should().BeTrue();
            File.ReadAllText(visualStudioConfigPath).Should().NotContain("wpf-devtools");

            using var json = JsonDocument.Parse(uninstall.Stdout);
            json.RootElement.GetProperty("removedInstallation").GetBoolean().Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_Uninstall_ShouldNotDeleteDefaultInstallRootWhenOnlyExternalRegistrationWasDetected()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var installRoot = Path.Combine(appData, "WpfDevToolsMcp");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");
            var defaultExecutable = Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(defaultExecutable)!);
            File.WriteAllText(defaultExecutable, "stub");
            Directory.CreateDirectory(Path.GetDirectoryName(visualStudioConfigPath)!);
            File.WriteAllText(
                visualStudioConfigPath,
                "{\"servers\":{\"wpf-devtools\":{\"command\":\"C:\\\\external\\\\wpf-devtools-x64.exe\",\"args\":[]}}}");

            var uninstall = RunInstaller(
                tempRoot,
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-Client", "visual-studio",
                    "-VisualStudioConfigPath", visualStudioConfigPath,
                    "-NonInteractive",
                    "-OutputJson"
                ]);

            uninstall.ExitCode.Should().Be(0, uninstall.Stderr);
            File.Exists(defaultExecutable).Should().BeTrue();
            File.ReadAllText(visualStudioConfigPath).Should().NotContain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
