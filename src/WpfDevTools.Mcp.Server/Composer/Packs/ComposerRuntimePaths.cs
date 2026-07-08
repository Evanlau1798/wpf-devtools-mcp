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

        var packagedRoot = ResolvePackagedComposerRoot(Directory.GetCurrentDirectory())
            ?? ResolvePackagedComposerRoot(AppContext.BaseDirectory);
        if (packagedRoot is not null)
        {
            return packagedRoot;
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

    private static string? ResolvePackagedComposerRoot(string seed)
    {
        var directory = new DirectoryInfo(seed);
        if (File.Exists(Path.Combine(directory.FullName, "manifest.json"))
            && string.Equals(directory.Name, "bin", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(directory.Parent?.FullName ?? directory.FullName);
        }

        if (File.Exists(Path.Combine(directory.FullName, "bin", "manifest.json")))
        {
            return Path.GetFullPath(directory.FullName);
        }

        return null;
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
