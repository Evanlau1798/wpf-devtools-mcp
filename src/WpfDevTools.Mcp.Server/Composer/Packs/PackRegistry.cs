using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Diagnostics;

namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal sealed class PackRegistry
{
    private readonly (PackScope Scope, string Root)[] _roots;

    public PackRegistry(string builtinRoot, string? projectRoot = null, string? userRoot = null)
    {
        _roots =
        [
            (PackScope.ProjectLocal, projectRoot ?? string.Empty),
            (PackScope.UserGlobal, userRoot ?? string.Empty),
            (PackScope.Builtin, builtinRoot)
        ];
    }

    public static PackRegistry ForRepository(string repoRoot)
        => new(ComposerPackPaths.BuiltinRoot(repoRoot));

    public PackRegistryResult ListPacks()
    {
        var packs = new Dictionary<string, PackRegistryItem>(StringComparer.Ordinal);
        var diagnostics = new List<string>();

        foreach (var (scope, root) in _roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var packRoot in Directory.EnumerateFiles(root, "pack.json", SearchOption.AllDirectories)
                         .Select(Path.GetDirectoryName)
                         .Where(path => path is not null)
                         .Select(path => path!))
            {
                if (IsUnderStagingRoot(root, packRoot))
                {
                    continue;
                }

                var item = TryLoadSafe(scope, packRoot, diagnostics);
                if (item is null)
                {
                    continue;
                }

                if (packs.TryGetValue(item.Id, out var existing))
                {
                    if (!string.Equals(existing.Version, item.Version, StringComparison.Ordinal))
                    {
                        diagnostics.Add($"Pack '{item.Id}' has multiple versions: {existing.Version}, {item.Version}.");
                    }

                    continue;
                }

                packs[item.Id] = item;
            }
        }

        return new PackRegistryResult(packs.Values.OrderBy(pack => pack.Id, StringComparer.Ordinal).ToArray(), diagnostics);
    }

    private static bool IsUnderStagingRoot(string root, string packRoot)
    {
        var stagingRoot = Path.GetFullPath(Path.Combine(root, ".staging"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedPackRoot = Path.GetFullPath(packRoot);
        return normalizedPackRoot.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static PackRegistryItem? TryLoadSafe(PackScope scope, string packRoot, List<string> diagnostics)
    {
        try
        {
            return TryLoad(scope, packRoot, diagnostics);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            diagnostics.Add($"Pack at {ComposerPathRedactor.RedactedPath} could not be loaded: {ComposerPathRedactor.Redact(ex.Message)}");
            return null;
        }
    }

    private static PackRegistryItem? TryLoad(PackScope scope, string packRoot, List<string> diagnostics)
    {
        var installManifestPath = Path.Combine(packRoot, "install.manifest.json");
        if (!File.Exists(installManifestPath))
        {
            throw new InvalidDataException("Pack root must contain install.manifest.json.");
        }

        var install = ComposerJsonLoader.Load<PackInstallManifest>(installManifestPath, UiComposerSchemaVersions.PackInstallManifest);
        if (!install.Enabled)
        {
            diagnostics.Add($"Pack '{install.Id}' {install.Version} is disabled.");
            return null;
        }

        var pack = ComposerPackLoader.Load(packRoot);
        if (!string.Equals(install.Id, pack.Manifest.Id, StringComparison.Ordinal)
            || !string.Equals(install.Version, pack.Manifest.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException("install.manifest.json id/version must match pack.json.");
        }

        var readinessPath = GetReadinessPath(packRoot, pack.Manifest.Id, pack.Manifest.Version);
        var readinessValid = readinessPath is not null && ReadValid(readinessPath);

        return new PackRegistryItem(
            pack.Manifest.Id,
            pack.Manifest.Version,
            scope,
            packRoot,
            pack.Blocks.Count,
            pack.Recipes.Count,
            pack.Examples.Count,
            pack.RendererTemplates.Count,
            readinessValid,
            pack.SourceLock.Sources.FirstOrDefault()?.Url ?? string.Empty,
            pack.Blocks.Select(block => block.Kind).Order(StringComparer.Ordinal).ToArray());
    }

    private static string? GetReadinessPath(string packRoot, string id, string version)
    {
        var directory = new DirectoryInfo(packRoot);
        var repoRoot = directory.Parent?.Parent?.Parent?.Parent?.FullName;
        if (repoRoot is null)
        {
            return null;
        }

        var candidate = Path.Combine(repoRoot, "packs", "baselines", id, version, "reports", $"{id}-{version}.readiness.json");
        return File.Exists(candidate) ? candidate : null;
    }

    private static bool ReadValid(string reportPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
        return document.RootElement.TryGetProperty("valid", out var valid) && valid.GetBoolean();
    }
}

internal sealed record PackRegistryResult(IReadOnlyList<PackRegistryItem> Packs, IReadOnlyList<string> Diagnostics);

internal sealed record PackRegistryItem(
    string Id,
    string Version,
    PackScope Scope,
    string RootPath,
    int BlockCount,
    int RecipeCount,
    int ExampleCount,
    int RendererCount,
    bool ReadinessValid,
    string SourceRepository,
    IReadOnlyList<string> BlockKinds);

internal enum PackScope
{
    ProjectLocal,
    UserGlobal,
    Builtin
}
