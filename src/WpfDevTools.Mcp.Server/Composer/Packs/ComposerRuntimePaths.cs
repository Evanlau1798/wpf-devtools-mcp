namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class ComposerRuntimePaths
{
    private const string ComposerRootEnvVar = "WPFDEVTOOLS_COMPOSER_ROOT";

    public static string ResolveComposerRoot()
    {
        var configured = Environment.GetEnvironmentVariable(ComposerRootEnvVar);
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        foreach (var candidate in EnumerateRootCandidates())
        {
            if (Directory.Exists(ComposerPackPaths.BuiltinRoot(candidate)))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(AppContext.BaseDirectory);
    }

    private static IEnumerable<string> EnumerateRootCandidates()
    {
        foreach (var seed in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(seed);
            while (current is not null)
            {
                yield return current.FullName;
                current = current.Parent;
            }
        }
    }
}
