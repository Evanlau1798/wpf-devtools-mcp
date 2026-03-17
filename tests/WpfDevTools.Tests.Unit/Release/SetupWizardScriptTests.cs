using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class SetupWizardScriptTests
{
    [Fact]
    public void SetupScript_ShouldDetectKnownClients()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fakeBin = Path.Combine(tempRoot, "bin");
            var codexLog = Path.Combine(tempRoot, "codex.log");
            var claudeLog = Path.Combine(tempRoot, "claude.log");
            ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "codex", codexLog);
            ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "claude", claudeLog);

            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            Directory.CreateDirectory(Path.Combine(appData, "Claude"));
            Directory.CreateDirectory(Path.Combine(appData, "Cursor", "User"));
            Directory.CreateDirectory(Path.Combine(localAppData, "Programs", "Cursor"));
            File.WriteAllText(Path.Combine(appData, "Claude", "claude_desktop_config.json"), "{}");
            File.WriteAllText(Path.Combine(appData, "Cursor", "User", "mcp.json"), "{}");
            File.WriteAllText(Path.Combine(localAppData, "Programs", "Cursor", "Cursor.exe"), "stub");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Setup-WpfDevTools.ps1"),
                new[] { "-DetectOnly", "-OutputJson" },
                new Dictionary<string, string?>
                {
                    ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            var detectedClients = json.RootElement.GetProperty("detectedClients").EnumerateArray().Select(x => x.GetString()).ToArray();
            detectedClients.Should().BeEquivalentTo(new[] { "claude-code", "claude-desktop", "codex", "cursor" });
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SetupScript_ShouldRegisterCliClientsAndEmitJsonSummary()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var codexLog = Path.Combine(tempRoot, "codex.log");
            var claudeLog = Path.Combine(tempRoot, "claude.log");
            ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "codex", codexLog);
            ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "claude", claudeLog);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Setup-WpfDevTools.ps1"),
                new[]
                {
                    "-PackagePath", packageDir,
                    "-InstallRoot", installRoot,
                    "-Clients", "codex,claude-code",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllText(codexLog).Should().Contain("mcp add wpf-devtools");
            File.ReadAllText(claudeLog).Should().Contain("mcp add --transport stdio wpf-devtools");

            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("installedExecutable").GetString()
                .Should().EndWith("x64\\current\\WpfDevTools.Mcp.Server.exe");
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
                .Should().BeEquivalentTo(new[] { "claude-code", "codex" });
            json.RootElement.GetProperty("registrations").EnumerateArray().Should().HaveCount(2);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SetupScript_ShouldEmitEmptySelectedClientsArrayWhenRegistrationIsSkipped()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var codexLog = Path.Combine(tempRoot, "codex.log");
            var claudeLog = Path.Combine(tempRoot, "claude.log");
            ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "codex", codexLog);
            ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "claude", claudeLog);

            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            Directory.CreateDirectory(Path.Combine(appData, "Cursor", "User"));
            Directory.CreateDirectory(Path.Combine(localAppData, "Programs", "Cursor"));
            File.WriteAllText(Path.Combine(appData, "Cursor", "User", "mcp.json"), "{}");
            File.WriteAllText(Path.Combine(localAppData, "Programs", "Cursor", "Cursor.exe"), "stub");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Setup-WpfDevTools.ps1"),
                new[]
                {
                    "-PackagePath", packageDir,
                    "-InstallRoot", installRoot,
                    "-Clients", "none",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("detectedClients").EnumerateArray().Should().NotBeEmpty();
            json.RootElement.GetProperty("selectedClients").ValueKind.Should().Be(JsonValueKind.Array);
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Should().BeEmpty();
            json.RootElement.GetProperty("registrations").EnumerateArray().Should().BeEmpty();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SetupScript_ShouldMergeDesktopClientConfigsAndCreateBackups()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var claudeConfigPath = Path.Combine(tempRoot, "config", "Claude", "claude_desktop_config.json");
            var cursorConfigPath = Path.Combine(tempRoot, "config", "Cursor", "User", "mcp.json");
            Directory.CreateDirectory(Path.GetDirectoryName(claudeConfigPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(cursorConfigPath)!);
            File.WriteAllText(claudeConfigPath, "{\"mcpServers\":{\"existing\":{\"command\":\"old.exe\",\"args\":[]}}}");
            File.WriteAllText(cursorConfigPath, "{\"servers\":{\"existing\":{\"command\":\"old.exe\",\"args\":[]}}}");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Setup-WpfDevTools.ps1"),
                new[]
                {
                    "-PackagePath", packageDir,
                    "-InstallRoot", installRoot,
                    "-Clients", "claude-desktop,cursor",
                    "-ClaudeDesktopConfigPath", claudeConfigPath,
                    "-CursorConfigPath", cursorConfigPath,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.ReadAllText(claudeConfigPath).Should().Contain("wpf-devtools").And.Contain("existing");
            File.ReadAllText(cursorConfigPath).Should().Contain("wpf-devtools").And.Contain("existing");
            Directory.GetFiles(Path.GetDirectoryName(claudeConfigPath)!, "claude_desktop_config.json.bak-*" ).Should().NotBeEmpty();
            Directory.GetFiles(Path.GetDirectoryName(cursorConfigPath)!, "mcp.json.bak-*" ).Should().NotBeEmpty();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SetupScript_ShouldUsePackageLocalInstallScriptWhenRunningFromPublishedPackage()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Install-WpfDevTools.ps1"), Path.Combine(packageDir, "install.ps1"), overwrite: true);
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Setup-WpfDevTools.ps1"), Path.Combine(packageDir, "setup.ps1"), overwrite: true);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(packageDir, "setup.ps1"),
                new[]
                {
                    "-InstallRoot", installRoot,
                    "-Clients", "none",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["PATH"] = string.Empty,
                    ["APPDATA"] = appData,
                    ["LOCALAPPDATA"] = localAppData,
                    ["USERPROFILE"] = userProfile
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("selectedClients").ValueKind.Should().Be(JsonValueKind.Array);
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Should().BeEmpty();
            File.Exists(Path.Combine(installRoot, "x64", "current", "WpfDevTools.Mcp.Server.exe")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
