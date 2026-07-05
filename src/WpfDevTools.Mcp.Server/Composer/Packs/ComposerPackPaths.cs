namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class ComposerPackPaths
{
    public static string BuiltinRoot(string composerRepoRoot)
        => Path.GetFullPath(Path.Combine(composerRepoRoot, "packs", "builtin"));

    public static string ProjectLocalRoot(string projectRoot)
        => Path.GetFullPath(Path.Combine(projectRoot, ".wpfdevtools", "packs"));

    public static string UserGlobalRoot(string localAppDataRoot)
        => Path.GetFullPath(Path.Combine(localAppDataRoot, "WpfDevTools", "Composer", "Packs"));
}
