using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class ComposerPackLoader
{
    private static readonly ConcurrentDictionary<string, CachedComposerPack> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ComposerPack Load(string packRoot)
    {
        var root = Path.GetFullPath(packRoot);
        var fingerprint = CreateFingerprint(root);
        if (Cache.TryGetValue(root, out var cached)
            && string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return cached.Pack;
        }

        try
        {
            var pack = LoadUncached(root);
            Cache[root] = new CachedComposerPack(fingerprint, pack);
            return pack;
        }
        catch
        {
            Cache.TryRemove(root, out _);
            throw;
        }
    }

    internal static void ClearCacheForTests()
        => Cache.Clear();

    internal static string GetFingerprint(string packRoot)
        => CreateFingerprint(Path.GetFullPath(packRoot));

    private static ComposerPack LoadUncached(string root)
    {
        var manifest = ComposerJsonLoader.Load<UiPackManifest>(
            Path.Combine(root, "pack.json"),
            UiComposerSchemaVersions.UiPack);
        var sourceLock = ComposerJsonLoader.Load<SourceLock>(
            Path.Combine(root, "source.lock.json"),
            UiComposerSchemaVersions.SourceLock);

        ValidatePathContract(root, manifest);

        var blocks = LoadDocuments<UiBlockDefinition>(
            Path.Combine(root, "blocks"),
            "*.block.json",
            UiComposerSchemaVersions.UiBlock);
        foreach (var block in blocks)
        {
            ValidateRendererTemplatePath(root, block);
        }

        var recipes = LoadDocuments<UiRecipeDefinition>(
            Path.Combine(root, "recipes"),
            "*.recipe.json",
            UiComposerSchemaVersions.UiRecipe);
        var examples = LoadDocuments<UiBlueprint>(
            Path.Combine(root, "examples"),
            "*.ui.json",
            UiComposerSchemaVersions.UiBlueprint);
        var rendererTemplates = Directory.Exists(Path.Combine(root, "renderers", "xaml"))
            ? Directory.GetFiles(Path.Combine(root, "renderers", "xaml"), "*.xaml.sbn")
                .Select(Path.GetFullPath)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];

        return new ComposerPack(manifest, sourceLock, blocks, recipes, examples, rendererTemplates);
    }

    private static string CreateFingerprint(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hash.AppendData([0]);
            hash.AppendData(File.ReadAllBytes(file));
            hash.AppendData([0]);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static T[] LoadDocuments<T>(string directory, string pattern, string schemaVersion)
        where T : ComposerJsonDocument, new()
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, pattern)
            .Order(StringComparer.Ordinal)
            .Select(path => ComposerJsonLoader.Load<T>(path, schemaVersion))
            .ToArray();
    }

    private static void ValidatePathContract(string packRoot, UiPackManifest manifest)
    {
        var versionDirectory = new DirectoryInfo(packRoot);
        if (!string.Equals(versionDirectory.Name, manifest.Version, StringComparison.Ordinal)
            || !string.Equals(versionDirectory.Parent?.Name, manifest.Id, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Pack root must be <pack-id>/<version> matching pack.json id '{manifest.Id}' and version '{manifest.Version}'.");
        }
    }

    private static void ValidateRendererTemplatePath(string packRoot, UiBlockDefinition block)
    {
        var template = block.Renderer.XamlTemplate;
        if (string.IsNullOrWhiteSpace(template) || Path.IsPathRooted(template))
        {
            throw new InvalidDataException($"Renderer template for block '{block.Kind}' must be a relative pack path.");
        }

        var fullTemplatePath = Path.GetFullPath(
            Path.Combine(packRoot, template.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnderRoot(packRoot, fullTemplatePath))
        {
            throw new InvalidDataException(
                $"Renderer template for block '{block.Kind}' escapes pack root: {template}.");
        }
    }

    private static bool IsUnderRoot(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record CachedComposerPack(string Fingerprint, ComposerPack Pack);

internal sealed record ComposerPack(
    UiPackManifest Manifest,
    SourceLock SourceLock,
    IReadOnlyList<UiBlockDefinition> Blocks,
    IReadOnlyList<UiRecipeDefinition> Recipes,
    IReadOnlyList<UiBlueprint> Examples,
    IReadOnlyList<string> RendererTemplates);
