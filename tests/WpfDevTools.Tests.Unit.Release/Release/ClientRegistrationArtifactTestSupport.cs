namespace WpfDevTools.Tests.Unit.Release;

internal static class ClientRegistrationArtifactTestSupport
{
    public static Dictionary<string, string?> CreateInstallerEnvironment(string tempRoot)
        => new()
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
        };

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