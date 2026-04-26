using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class SetupWizardScriptTests
{
    [Fact]
    public void OnlineInstaller_ShouldApplyClaudeCodeRegistrationViaCliAndReportAppliedStatus()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var claudeLog = Path.Combine(tempRoot, "claude.log");
            ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "claude", claudeLog);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "claude-code",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot, fakeBin));

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllText(claudeLog)
                .Should().Contain("mcp add --transport stdio wpf-devtools")
                .And.Contain("wpf-devtools-x64.exe");

            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
                .Should().BeEquivalentTo(["claude-code"]);
            var registration = json.RootElement.GetProperty("registrations").EnumerateArray().Single();
            registration.GetProperty("mode").GetString().Should().Be("cli");
            registration.GetProperty("applied").GetBoolean().Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldApplyCodexRegistrationViaCliAndReportAppliedStatus()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var codexLog = Path.Combine(tempRoot, "codex.log");
            ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "codex", codexLog);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "codex",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot, fakeBin));

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllText(codexLog)
                .Should().Contain("mcp add wpf-devtools")
                .And.Contain("wpf-devtools-x64.exe");

            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
                .Should().BeEquivalentTo(["codex"]);
            var registration = json.RootElement.GetProperty("registrations").EnumerateArray().Single();
            registration.GetProperty("mode").GetString().Should().Be("cli");
            registration.GetProperty("applied").GetBoolean().Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldRejectElevatedCliRegistrationWithoutTrustedAbsolutePath()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var codexLog = Path.Combine(tempRoot, "codex.log");
            ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "codex", codexLog);

            var environment = CreateInstallerEnvironment(tempRoot, fakeBin);
            environment["WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED"] = "1";

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "codex",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                environment);

            result.ExitCode.Should().NotBe(0);
            File.Exists(codexLog).Should().BeFalse();
            result.Stderr.Should().Contain("WPFDEVTOOLS_SKIP_ELEVATION=1");
            result.Stderr.Should().Contain("WPFDEVTOOLS_CODEX_COMMAND_PATH");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldMergeVsCodeConfigAndCreateBackup()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var configPath = Path.Combine(appData, "Code", "User", "mcp.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, "{\"servers\":{\"existing\":{\"command\":\"old.exe\",\"args\":[]}}}");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "vscode",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot, null));

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllText(configPath).Should().Contain("existing").And.Contain("wpf-devtools");
            Directory.GetFiles(Path.GetDirectoryName(configPath)!, "mcp.json.bak-*").Should().NotBeEmpty();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PackageLocalInstallScript_ShouldRunInOfflineModeFromReleaseFolder()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot);
            var packageBinDir = Path.Combine(packageDir, "bin");
            var installRoot = Path.Combine(tempRoot, "install-root");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                Path.Combine(packageBinDir, "install.ps1"),
                overwrite: true);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(packageBinDir, "install.ps1"),
                new[]
                {
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                CreateInstallerEnvironment(tempRoot, null));

            result.ExitCode.Should().Be(0, result.Stderr);
            File.Exists(Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe")).Should().BeTrue();
            File.ReadAllText(Path.Combine(tempRoot, "UserProfile", ".mcp.json"))
                .Should().Contain("\"servers\"")
                .And.Contain("wpf-devtools-x64.exe");

            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("mode").GetString().Should().Be("offline");
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
                .Should().BeEquivalentTo(["visual-studio"]);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static Dictionary<string, string?> CreateInstallerEnvironment(string tempRoot, string? fakeBin)
    {
        var environment = new Dictionary<string, string?>
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
        };

        if (!string.IsNullOrWhiteSpace(fakeBin))
        {
            environment["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        }

        return environment;
    }
}
