using System.Text.Json;
using System.IO.Compression;
using FluentAssertions;
using Xunit;
using static WpfDevTools.Tests.Unit.Release.InstallerScriptTestSupport;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("TimingSensitive")]
public sealed partial class InstallerScriptTests
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
                CreateInstallerEnvironment(
                    tempRoot,
                    new Dictionary<string, string?>
                    {
                        ["WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY"] = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer")
                    }));

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
    public void OnlineInstaller_ShouldPersistResolvedVersionExecutableAndVerificationMetadataInState()
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

            using var json = JsonDocument.Parse(result.Stdout);
            var statePath = json.RootElement.GetProperty("statePath").GetString();
            statePath.Should().NotBeNullOrWhiteSpace();

            using var stateDocument = JsonDocument.Parse(File.ReadAllText(statePath!));
            var registration = stateDocument.RootElement
                .GetProperty("registrations")
                .GetProperty("other");

            registration.GetProperty("resolvedVersion").GetString().Should().Be("1.2.3");
            registration.GetProperty("installedExecutable").GetString()
                .Should().EndWith(Path.Combine("current", "bin", "wpf-devtools-x64.exe"));
            registration.GetProperty("lastVerifiedUtc").ValueKind.Should().NotBe(JsonValueKind.Undefined);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldVerifyClaudeCodeRegistrationWithCliListBeforePersistingState()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var claudeLog = Path.Combine(tempRoot, "claude.log");
            Directory.CreateDirectory(fakeBin);
            var expectedExecutable = Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe");
            File.WriteAllText(
                Path.Combine(fakeBin, "claude.cmd"),
                string.Join(
                    Environment.NewLine,
                    [
                        "@echo off",
                        "echo %*>>\"" + claudeLog + "\"",
                        "if /I \"%1 %2\"==\"mcp list\" echo wpf-devtools " + expectedExecutable,
                        "exit /b 0"
                    ]));

            var result = RunInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "claude-code",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                new Dictionary<string, string?>
                {
                    ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllText(claudeLog)
                .Should().Contain("mcp add --transport stdio wpf-devtools")
                .And.Contain("mcp list");

            using var json = JsonDocument.Parse(result.Stdout);
            using var stateDocument = JsonDocument.Parse(File.ReadAllText(json.RootElement.GetProperty("statePath").GetString()!));
            var registration = stateDocument.RootElement
                .GetProperty("registrations")
                .GetProperty("claude-code");
            registration.GetProperty("lastVerifiedUtc").GetString().Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_NonInteractiveAndJsonModes_ShouldNotDependOnTuiSessionState()
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
            result.Stdout.Should().NotContain("HomeScreen");
            result.Stdout.Should().NotContain("ProgressScreen");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void DiscoveryParser_ShouldIgnoreExecutablePathsFromOtherCliEntries()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var expectedExecutable = Path.Combine(tempRoot, "install-root", "x64", "current", "bin", "wpf-devtools-x64.exe");
            var unrelatedExecutable = Path.Combine(tempRoot, "other-tool", "wpf-devtools-x64.exe");

            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Discovery.ps1").Replace("'", "''") + "'",
                    "$text = @'",
                    "other-server " + unrelatedExecutable.Replace("'", "''"),
                    "wpf-devtools " + expectedExecutable.Replace("'", "''"),
                    "'@",
                    "Get-WpfDevToolsExecutableFromText -Text $text"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be(expectedExecutable);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void VerificationParser_ShouldExtractExecutableFromQuotedWpfDevToolsEntry()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var expectedExecutable = Path.Combine(tempRoot, "install-root", "x64", "current", "bin", "wpf-devtools-x64.exe");

            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Verification.ps1").Replace("'", "''") + "'",
                    "$text = @'",
                    "Registered MCP servers:",
                    "- \"wpf-devtools\": \"" + expectedExecutable.Replace("'", "''") + "\"",
                    "'@",
                    "Get-VerifiedWpfDevToolsExecutableFromText -Text $text"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be(expectedExecutable);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void VerificationParser_ShouldIgnoreMatchingExecutableWhenItBelongsToDifferentNamedEntry()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var expectedExecutable = Path.Combine(tempRoot, "install-root", "x64", "current", "bin", "wpf-devtools-x64.exe");

            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Verification.ps1").Replace("'", "''") + "'",
                    "$text = @'",
                    "- other-server: \"" + expectedExecutable.Replace("'", "''") + "\"",
                    "- wpf-devtools: C:\\stale\\wpf-devtools-x64.exe",
                    "'@",
                    "Get-VerifiedWpfDevToolsExecutableFromText -Text $text"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be(@"C:\stale\wpf-devtools-x64.exe");
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
