using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

internal static class StandaloneInstallerRegressionTestSupport
{
    public static TheoryData<string> RemovalActions => new()
    {
        "uninstall",
        "full-uninstall"
    };

    public static (int ExitCode, string Stdout, string Stderr) RunRepoInstaller(
        string tempRoot,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment = null)
        => ReleaseScriptTestHarness.RunPowerShellScript(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
            arguments,
            environment ?? CreateStandaloneEnvironment(tempRoot));

    public static IReadOnlyDictionary<string, string?> CreateStandaloneEnvironment(
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

    public static IReadOnlyDictionary<string, string?> CreatePublicStandaloneEnvironment(
        string tempRoot,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var environment = new Dictionary<string, string?>
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
            ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
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

    public static (string StateKey, string ConfigPath, string[] InstallArguments, string[] UninstallArguments, string[] FullUninstallArguments) CreateMalformedJsonClientScenario(
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
            "claude-desktop" => (
                "claude-desktop",
                Path.Combine(tempRoot, "config", "Claude", "claude_desktop_config.json"),
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "claude-desktop",
                    "-ClaudeDesktopConfigPath", Path.Combine(tempRoot, "config", "Claude", "claude_desktop_config.json"),
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                [
                    "-Action", "uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-Client", "claude-desktop",
                    "-ClaudeDesktopConfigPath", Path.Combine(tempRoot, "config", "Claude", "claude_desktop_config.json"),
                    "-NonInteractive",
                    "-OutputJson"
                ],
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-ClaudeDesktopConfigPath", Path.Combine(tempRoot, "config", "Claude", "claude_desktop_config.json"),
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
