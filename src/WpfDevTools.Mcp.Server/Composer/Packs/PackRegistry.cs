using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Diagnostics;

namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal sealed class PackRegistry
{
    private const int MaxVisitedDirectories = 4096;
    private const int MaxPackCandidates = 256;
    private const int MaxDiagnostics = 64;
    private readonly (PackScope Scope, string Root)[] _roots;

    public PackRegistry(string builtinRoot, string? projectRoot = null, string? userRoot = null)
    {
        _roots =
        [
            (PackScope.ProjectLocal, NormalizeRoot(projectRoot, nameof(projectRoot))),
            (PackScope.UserGlobal, NormalizeRoot(userRoot, nameof(userRoot))),
            (PackScope.Builtin, ComposerLocalPathPolicy.RequireLocalRoot(builtinRoot, nameof(builtinRoot)))
        ];
    }

    public static PackRegistry ForRepository(string repoRoot)
        => new(ComposerPackPaths.BuiltinRoot(repoRoot));

    private static string NormalizeRoot(string? root, string parameterName)
        => string.IsNullOrWhiteSpace(root)
            ? string.Empty
            : ComposerLocalPathPolicy.RequireLocalRoot(root, parameterName);

    public PackRegistryResult ListPacks(CancellationToken cancellationToken = default)
    {
        var packs = new Dictionary<string, PackRegistryItem>(StringComparer.Ordinal);
        var diagnostics = new List<string>();

        foreach (var (scope, root) in _roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var packRoot in EnumeratePackRoots(root, cancellationToken))
            {
                var item = TryLoadSafe(scope, packRoot, diagnostics);
                if (item is null)
                {
                    continue;
                }

                if (packs.TryGetValue(item.Id, out var existing))
                {
                    if (!string.Equals(existing.Version, item.Version, StringComparison.Ordinal))
                    {
                        AddDiagnostic(diagnostics, $"Pack '{item.Id}' has multiple versions: {existing.Version}, {item.Version}.");
                    }

                    continue;
                }

                packs[item.Id] = item;
            }
        }

        return new PackRegistryResult(packs.Values.OrderBy(pack => pack.Id, StringComparer.Ordinal).ToArray(), diagnostics);
    }

    private static IEnumerable<string> EnumeratePackRoots(string root, CancellationToken cancellationToken)
    {
        if (IsReparsePoint(root))
        {
            throw new InvalidDataException("Pack discovery root must not be a reparse point.");
        }

        var visitedDirectories = 0;
        var packCandidates = 0;
        foreach (var packIdRoot in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDirectoryBudget(ref visitedDirectories);
            if (string.Equals(Path.GetFileName(packIdRoot), ".staging", StringComparison.OrdinalIgnoreCase)
                || IsReparsePoint(packIdRoot))
            {
                continue;
            }

            foreach (var versionRoot in Directory.EnumerateDirectories(packIdRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureDirectoryBudget(ref visitedDirectories);
                if (IsReparsePoint(versionRoot)
                    || !File.Exists(Path.Combine(versionRoot, "pack.json")))
                {
                    continue;
                }

                if (++packCandidates > MaxPackCandidates)
                {
                    throw new InvalidDataException($"Pack discovery candidate limit of {MaxPackCandidates} was exceeded.");
                }

                yield return versionRoot;
            }
        }
    }

    private static void EnsureDirectoryBudget(ref int visitedDirectories)
    {
        if (++visitedDirectories > MaxVisitedDirectories)
        {
            throw new InvalidDataException($"Pack discovery directory limit of {MaxVisitedDirectories} was exceeded.");
        }
    }

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void AddDiagnostic(List<string> diagnostics, string message)
    {
        if (diagnostics.Count < MaxDiagnostics)
        {
            diagnostics.Add(message);
        }
    }

    private static PackRegistryItem? TryLoadSafe(PackScope scope, string packRoot, List<string> diagnostics)
    {
        try
        {
            return TryLoad(scope, packRoot, diagnostics);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            AddDiagnostic(diagnostics, $"Pack at {ComposerPathRedactor.RedactedPath} could not be loaded: {ComposerPathRedactor.Redact(ex.Message)}");
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
            AddDiagnostic(diagnostics, $"Pack '{install.Id}' {install.Version} is disabled.");
            return null;
        }

        var loaded = ComposerPackLoader.LoadWithFingerprint(packRoot);
        var pack = loaded.Pack;
        if (!string.Equals(install.Id, pack.Manifest.Id, StringComparison.Ordinal)
            || !string.Equals(install.Version, pack.Manifest.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException("install.manifest.json id/version must match pack.json.");
        }

        var readinessPath = GetReadinessPath(packRoot, pack.Manifest.Id, pack.Manifest.Version);
        var readinessValid = readinessPath is not null && ReadValid(readinessPath);
        var rolePlan = ComposerPackKindRoleResolver.Resolve(pack.Manifest.Kind);

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
            pack.Blocks.Select(block => block.Kind).Order(StringComparer.Ordinal).ToArray(),
            rolePlan.Role,
            rolePlan.Required,
            pack.Manifest.Kind)
        {
            Fingerprint = loaded.Fingerprint,
            ThemeTokens = pack.Manifest.ThemeTokens,
            ResourceVariants = PackResourceVariantResolver.Describe(pack.Manifest)
        };
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
    IReadOnlyList<string> BlockKinds,
    string Role = "",
    bool Required = false,
    string Kind = "")
{
    public string Fingerprint { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> ThemeTokens { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    public PackResourceVariantCatalog ResourceVariants { get; init; } = new(string.Empty, []);
}

internal enum PackScope
{
    ProjectLocal,
    UserGlobal,
    Builtin
}
