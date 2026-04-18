using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerIdeRegistrationShimBackedTests
{
    [Theory]
    [InlineData("claude-code", null)]
    [InlineData("codex", null)]
    [InlineData("vscode", null)]
    [InlineData("visual-studio", null)]
    [InlineData("claude-desktop", null)]
    [InlineData("cursor", "global")]
    [InlineData("cursor", "project")]
    public void OnlineInstaller_ShouldInstallAndUninstallShimBackedClientRegistrationsInTempRoots(
        string client,
        string? cursorMode)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            var scenario = CreateScenario(tempRoot, client, cursorMode);
            var environment = new Dictionary<string, string?>
            {
                ["APPDATA"] = appData,
                ["LOCALAPPDATA"] = localAppData,
                ["USERPROFILE"] = userProfile
            };
            ConfigureScenarioEnvironment(tempRoot, scenario, environment);

            var installArguments = new List<string>
            {
                "-PackageArchivePath", archivePath,
                "-InstallRoot", installRoot,
                "-Client", client,
                "-NonInteractive",
                "-Force",
                "-OutputJson"
            };
            installArguments.AddRange(scenario.InstallArguments);

            var installResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                installArguments,
                environment);

            installResult.ExitCode.Should().Be(0, installResult.Stderr);
            using (var installJson = JsonDocument.Parse(installResult.Stdout))
            {
                installJson.RootElement.GetProperty("verificationMessage").GetString().Should().NotBeNullOrWhiteSpace();
            }

            AssertInstalledScenarioEvidence(scenario);

            var uninstallArguments = new List<string>
            {
                "-Action", "uninstall",
                "-InstallRoot", installRoot,
                "-Architecture", "x64",
                "-Client", client,
                "-NonInteractive",
                "-OutputJson"
            };
            uninstallArguments.AddRange(scenario.UninstallArguments);

            var uninstallResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                uninstallArguments,
                environment);

            uninstallResult.ExitCode.Should().Be(0, uninstallResult.Stderr);
            using (var uninstallJson = JsonDocument.Parse(uninstallResult.Stdout))
            {
                uninstallJson.RootElement.GetProperty("verificationMessage").GetString().Should().NotBeNullOrWhiteSpace();
            }

            AssertUninstalledScenarioEvidence(scenario);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldRejectCliShimVerificationWhenListOutputPointsAtDifferentExecutable()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var cliDirectory = Path.Combine(tempRoot, "bin");
            var logPath = Path.Combine(tempRoot, "cli-state", "claude.log");
            Directory.CreateDirectory(cliDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(
                Path.Combine(cliDirectory, "claude.cmd"),
                BuildCliRegistrationShimWithListOutput(
                    logPath,
                    @"wpf-devtools C:\stale\wpf-devtools-x64.exe"));

            var environment = new Dictionary<string, string?>
            {
                ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                ["PATH"] = cliDirectory + ";" + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            };

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "claude-code",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment);

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("does not match");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_ShouldRejectCliShimVerificationWhenExpectedPathBelongsToDifferentEntry()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var cliDirectory = Path.Combine(tempRoot, "bin");
            var logPath = Path.Combine(tempRoot, "cli-state", "claude.log");
            var expectedPath = Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe");
            Directory.CreateDirectory(cliDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(
                Path.Combine(cliDirectory, "claude.cmd"),
                BuildCliRegistrationShimWithListOutput(
                    logPath,
                    "other-server " + expectedPath + Environment.NewLine +
                    @"wpf-devtools C:\stale\wpf-devtools-x64.exe"));

            var environment = new Dictionary<string, string?>
            {
                ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                ["PATH"] = cliDirectory + ";" + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            };

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "claude-code",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment);

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("does not match");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static void ConfigureScenarioEnvironment(
        string tempRoot,
        RegistrationScenario scenario,
        IDictionary<string, string?> environment)
    {
        if (string.IsNullOrWhiteSpace(scenario.CommandName))
        {
            return;
        }

        var shimDirectory = Path.Combine(tempRoot, "bin");
        Directory.CreateDirectory(shimDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(scenario.StatePath!)!);
        File.WriteAllText(
            Path.Combine(shimDirectory, scenario.CommandName + ".cmd"),
            BuildCliRegistrationShim(
                statePath: scenario.StatePath!,
                logPath: scenario.LogPath!));

        environment["PATH"] = shimDirectory + ";" + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
    }

    private static void AssertInstalledScenarioEvidence(RegistrationScenario scenario)
    {
        if (!string.IsNullOrWhiteSpace(scenario.ConfigPath))
        {
            File.Exists(scenario.ConfigPath).Should().BeTrue();
            File.ReadAllText(scenario.ConfigPath).Should().Contain("wpf-devtools");
            return;
        }

        File.Exists(scenario.StatePath).Should().BeTrue();
        File.ReadAllText(scenario.StatePath!).Should().Contain("wpf-devtools");
        File.ReadAllText(scenario.LogPath!).Should().Contain("mcp add").And.Contain("mcp list");
    }

    private static void AssertUninstalledScenarioEvidence(RegistrationScenario scenario)
    {
        if (!string.IsNullOrWhiteSpace(scenario.ConfigPath))
        {
            if (File.Exists(scenario.ConfigPath))
            {
                File.ReadAllText(scenario.ConfigPath).Should().NotContain("wpf-devtools");
            }

            return;
        }

        File.Exists(scenario.StatePath).Should().BeFalse();
        File.ReadAllText(scenario.LogPath!).Should().Contain("mcp remove");
    }

    private static RegistrationScenario CreateScenario(string tempRoot, string client, string? cursorMode)
    {
        return (client, cursorMode) switch
        {
            ("claude-code", _) => CreateCliScenario(tempRoot, "claude"),
            ("codex", _) => CreateCliScenario(tempRoot, "codex"),
            ("vscode", _) => new RegistrationScenario(
                ConfigPath: Path.Combine(tempRoot, "config", "vscode", "mcp.json"),
                ["-VsCodeConfigPath", Path.Combine(tempRoot, "config", "vscode", "mcp.json")],
                ["-VsCodeConfigPath", Path.Combine(tempRoot, "config", "vscode", "mcp.json")]),
            ("visual-studio", _) => new RegistrationScenario(
                ConfigPath: Path.Combine(tempRoot, "config", "visual-studio", ".mcp.json"),
                ["-VisualStudioConfigPath", Path.Combine(tempRoot, "config", "visual-studio", ".mcp.json")],
                ["-VisualStudioConfigPath", Path.Combine(tempRoot, "config", "visual-studio", ".mcp.json")]),
            ("claude-desktop", _) => new RegistrationScenario(
                ConfigPath: Path.Combine(tempRoot, "config", "claude-desktop", "claude_desktop_config.json"),
                ["-ClaudeDesktopConfigPath", Path.Combine(tempRoot, "config", "claude-desktop", "claude_desktop_config.json")],
                ["-ClaudeDesktopConfigPath", Path.Combine(tempRoot, "config", "claude-desktop", "claude_desktop_config.json")]),
            ("cursor", "global") => new RegistrationScenario(
                ConfigPath: Path.Combine(tempRoot, "config", "cursor", "global", "mcp.json"),
                ["-CursorMode", "global", "-CursorConfigPath", Path.Combine(tempRoot, "config", "cursor", "global", "mcp.json")],
                ["-CursorMode", "global", "-CursorConfigPath", Path.Combine(tempRoot, "config", "cursor", "global", "mcp.json")]),
            ("cursor", "project") => new RegistrationScenario(
                ConfigPath: Path.Combine(tempRoot, "CursorProject", ".cursor", "mcp.json"),
                ["-CursorMode", "project", "-CursorProjectRoot", Path.Combine(tempRoot, "CursorProject")],
                ["-CursorMode", "project", "-CursorProjectRoot", Path.Combine(tempRoot, "CursorProject")]),
            _ => throw new ArgumentOutOfRangeException(nameof(client), $"{client}/{cursorMode}")
        };
    }

    private static RegistrationScenario CreateCliScenario(string tempRoot, string commandName)
    {
        var cliDirectory = Path.Combine(tempRoot, "cli-state");
        return new RegistrationScenario(
            ConfigPath: null,
            InstallArguments: [],
            UninstallArguments: [],
            CommandName: commandName,
            StatePath: Path.Combine(cliDirectory, commandName + ".state"),
            LogPath: Path.Combine(cliDirectory, commandName + ".log"));
    }

    private static string BuildCliRegistrationShim(string statePath, string logPath)
    {
        static string EscapeForBatch(string value) => value.Replace("\"", "\"\"");

        return string.Join(
            Environment.NewLine,
            [
                "@echo off",
                "setlocal EnableDelayedExpansion",
                "set \"STATE_PATH=" + EscapeForBatch(statePath) + "\"",
                "set \"LOG_PATH=" + EscapeForBatch(logPath) + "\"",
                "if not exist \"%LOG_PATH%\" type nul >\"%LOG_PATH%\"",
                "echo %*>>\"%LOG_PATH%\"",
                "if /I \"%1 %2\"==\"mcp add\" (",
                "  for %%A in (%*) do set \"LAST_ARG=%%~A\"",
                "  >\"%STATE_PATH%\" echo wpf-devtools !LAST_ARG!",
                "  exit /b 0",
                ")",
                "if /I \"%1 %2\"==\"mcp remove\" (",
                "  if exist \"%STATE_PATH%\" del /f /q \"%STATE_PATH%\"",
                "  exit /b 0",
                ")",
                "if /I \"%1 %2\"==\"mcp list\" (",
                "  if exist \"%STATE_PATH%\" type \"%STATE_PATH%\"",
                "  exit /b 0",
                ")",
                "exit /b 0"
            ]);
    }

    private static string BuildCliRegistrationShimWithListOutput(string logPath, string listOutput)
    {
        static string EscapeForBatch(string value) => value.Replace("\"", "\"\"");

        var lines = new List<string>
        {
            "@echo off",
            "setlocal",
            "set \"LOG_PATH=" + EscapeForBatch(logPath) + "\"",
            "if not exist \"%LOG_PATH%\" type nul >\"%LOG_PATH%\"",
            "echo %*>>\"%LOG_PATH%\"",
            "if /I \"%1 %2\"==\"mcp add\" exit /b 0",
            "if /I \"%1 %2\"==\"mcp list\" ("
        };
        foreach (var outputLine in listOutput.Split([Environment.NewLine], StringSplitOptions.None))
        {
            lines.Add("  echo " + EscapeForBatch(outputLine));
        }

        lines.Add("  exit /b 0");
        lines.Add(")");
        lines.Add("exit /b 0");
        return string.Join(Environment.NewLine, lines);
    }

    private sealed record RegistrationScenario(
        string? ConfigPath,
        IReadOnlyList<string> InstallArguments,
        IReadOnlyList<string> UninstallArguments,
        string? CommandName = null,
        string? StatePath = null,
        string? LogPath = null);
}
