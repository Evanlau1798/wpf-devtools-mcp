using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerScriptTests
{
    [Fact]
    public void OnlineInstaller_ShouldCreateClientRegistrationArtifactsUnderInstallBase()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");

            var result = RunInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);

            result.ExitCode.Should().Be(0, result.Stderr);
            var installBase = Path.Combine(installRoot, "x64");
            var registrationDir = Path.Combine(installBase, "client-registration");
            File.Exists(Path.Combine(installBase, "install-manifest.json")).Should().BeTrue();
            Directory.Exists(registrationDir).Should().BeTrue();
            File.ReadAllText(Path.Combine(registrationDir, "vscode.json"))
                .Should().Contain("\"servers\"")
                .And.Contain("wpf-devtools-x64.exe");
            File.ReadAllText(Path.Combine(registrationDir, "other.mcpServers.json"))
                .Should().Contain("\"mcpServers\"")
                .And.Contain("wpf-devtools-x64.exe");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackageLocalInstaller_ShouldFailWhenServerExecutableIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = Path.Combine(tempRoot, "package");
            var packageBinDir = Path.Combine(packageDir, "bin");
            Directory.CreateDirectory(packageBinDir);
            File.WriteAllText(
                Path.Combine(packageBinDir, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    name = "wpf-devtools",
                    version = "1.2.3",
                    architecture = "x64",
                    runtimeId = "win-x64"
                }));
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                Path.Combine(packageBinDir, "install.ps1"),
                overwrite: true);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(packageBinDir, "install.ps1"),
                ["-InstallRoot", Path.Combine(tempRoot, "install-root"), "-Client", "other", "-NonInteractive", "-Force", "-OutputJson"],
                CreateInstallerEnvironment(tempRoot));

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("Package does not contain an executable");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldWriteAbsolutePathsWhenInstallRootIsRelative()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        var relativeInstallRoot = Path.Combine("tmp", "relative-install", Guid.NewGuid().ToString("N"));
        var absoluteInstallRoot = ReleaseScriptTestHarness.GetRepoFilePath(relativeInstallRoot);

        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var result = RunInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", relativeInstallRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);

            result.ExitCode.Should().Be(0, result.Stderr);
            var registrationDir = Path.Combine(absoluteInstallRoot, "x64", "client-registration");
            var expectedExecutable = Path.Combine(absoluteInstallRoot, "x64", "current", "bin", "wpf-devtools-x64.exe");

            using var vscodeDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(registrationDir, "vscode.json")));
            using var otherDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(registrationDir, "other.mcpServers.json")));
            vscodeDocument.RootElement.GetProperty("servers").GetProperty("wpf-devtools").GetProperty("command").GetString()
                .Should().Be(expectedExecutable);
            otherDocument.RootElement.GetProperty("mcpServers").GetProperty("wpf-devtools").GetProperty("command").GetString()
                .Should().Be(expectedExecutable);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
            ReleaseScriptTestHarness.DeleteDirectory(absoluteInstallRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldReuseExistingBinaryOnRepeatedInstallIntoSameRoot()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var vscodeConfigPath = Path.Combine(tempRoot, "config", "Code", "User", "mcp.json");

            var first = RunInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-VsCodeConfigPath", vscodeConfigPath,
                    "-NonInteractive",
                    "-OutputJson"
                ]);
            first.ExitCode.Should().Be(0, first.Stderr);

            var second = RunInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-VsCodeConfigPath", vscodeConfigPath,
                    "-NonInteractive",
                    "-OutputJson"
                ]);
            second.ExitCode.Should().Be(0, second.Stderr);

            using var firstJson = JsonDocument.Parse(first.Stdout);
            using var secondJson = JsonDocument.Parse(second.Stdout);
            secondJson.RootElement.GetProperty("reusedExistingBinary").GetBoolean().Should().BeTrue();
            secondJson.RootElement.GetProperty("installedExecutable").GetString()
                .Should().Be(firstJson.RootElement.GetProperty("installedExecutable").GetString());
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

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
    public void OnlineInstaller_Uninstall_ShouldRemoveArchitectureDirectoryWhenLastRegistrationIsRemoved()
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
            Directory.Exists(Path.Combine(installRoot, "x64")).Should().BeFalse();
            File.ReadAllText(visualStudioConfigPath).Should().NotContain("wpf-devtools");

            using var json = JsonDocument.Parse(uninstall.Stdout);
            json.RootElement.GetProperty("removedInstallation").GetBoolean().Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunInstaller(
        string tempRoot,
        IReadOnlyList<string> arguments)
        => ReleaseScriptTestHarness.RunPowerShellScript(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
            arguments,
            CreateInstallerEnvironment(tempRoot));

    private static Dictionary<string, string?> CreateInstallerEnvironment(string tempRoot)
        => new()
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
        };
}
