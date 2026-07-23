using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class ComposerPackLoader
{
    private static readonly ConcurrentDictionary<string, CachedComposerPack> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ComposerPack Load(string packRoot)
        => LoadWithFingerprint(packRoot).Pack;

    internal static ComposerPackLoadResult LoadWithFingerprint(string packRoot)
    {
        var root = Path.GetFullPath(packRoot);
        var observedFingerprint = CreateFingerprint(root);
        if (Cache.TryGetValue(root, out var cached)
            && string.Equals(cached.Fingerprint, observedFingerprint, StringComparison.Ordinal))
        {
            return new ComposerPackLoadResult(cached.Pack, cached.Fingerprint, FromCache: true);
        }

        try
        {
            var pack = LoadUncached(root);
            var loadedFingerprint = CreateFingerprint(root);
            if (!string.Equals(observedFingerprint, loadedFingerprint, StringComparison.Ordinal))
            {
                throw new IOException("Pack content changed while it was being loaded.");
            }

            Cache[root] = new CachedComposerPack(loadedFingerprint, pack);
            return new ComposerPackLoadResult(pack, loadedFingerprint, FromCache: false);
        }
        catch
        {
            Cache.TryRemove(root, out _);
            throw;
        }
    }

    internal static ComposerPack LoadUncachedForValidation(string packRoot)
        => LoadUncached(Path.GetFullPath(packRoot));

    internal static void ClearCacheForTests()
        => Cache.Clear();

    internal static int CachedPackCountForTests()
        => Cache.Count;

    internal static string[] CachedPackRootsForTests()
        => Cache.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

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
        PackResourceVariantResolver.Validate(manifest);

        var blocks = LoadDocuments<UiBlockDefinition>(
            Path.Combine(root, "blocks"),
            "*.block.json",
            UiComposerSchemaVersions.UiBlock);
        PackPropertyVocabularyLoader.Hydrate(root, blocks);
        ValidateBlockContracts(manifest, blocks);
        foreach (var block in blocks)
        {
            ValidateRendererTemplatePath(root, block);
            ValidateCodeBehindBaseType(block);
            ValidateRendererNameScopeElements(block);
            ValidateInteractionContract(block);
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

    private static void ValidateBlockContracts(
        UiPackManifest manifest,
        IReadOnlyList<UiBlockDefinition> blocks)
    {
        var ownedPrefix = manifest.Id + ".";
        var foreignKind = blocks.FirstOrDefault(block =>
            !block.Kind.StartsWith(ownedPrefix, StringComparison.Ordinal));
        if (foreignKind is not null)
        {
            throw new InvalidDataException(
                $"BlockKindOwnershipMismatch: block '{foreignKind.Kind}' is not owned by pack '{manifest.Id}'.");
        }

        var duplicateKind = blocks.GroupBy(block => block.Kind, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateKind is not null)
        {
            throw new InvalidDataException(
                $"DuplicateBlockKind: pack '{manifest.Id}' contains duplicate block kind '{duplicateKind}'.");
        }

        var loadedKinds = blocks.Select(block => block.Kind)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var declaredKinds = manifest.Blocks.Order(StringComparer.Ordinal).ToArray();
        if (!loadedKinds.SequenceEqual(declaredKinds, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"BlockManifestMismatch: loaded=[{string.Join(", ", loadedKinds)}]; declared=[{string.Join(", ", declaredKinds)}].");
        }

        foreach (var block in blocks)
        {
            ValidateAuthoringSemantics(block);
            foreach (var (slotName, slot) in block.Slots)
            {
                if (slot.MinItems < 0
                    || slot.MaxItems is int maxItems && (maxItems < 0 || maxItems < slot.MinItems))
                {
                    throw new InvalidDataException(
                        $"InvalidSlotItemBounds: block '{block.Kind}' slot '{slotName}' requires 0 <= minItems <= maxItems when maxItems is declared.");
                }

                ValidateAdjacencyAdvisory(block, slotName, slot.AdjacencyAdvisory);
            }
        }
    }

    private static void ValidateAuthoringSemantics(UiBlockDefinition block)
    {
        var roles = block.AuthoringRoles;
        if (roles is { Length: <= 16 }
            && roles.All(IsSafeSemanticToken)
            && roles.Distinct(StringComparer.Ordinal).Count() == roles.Length)
        {
            return;
        }

        throw new InvalidDataException(
            $"InvalidAuthoringSemantics: block '{block.Kind}' roles must contain at most 16 unique semantic tokens.");
    }

    private static void ValidateAdjacencyAdvisory(
        UiBlockDefinition block,
        string slotName,
        UiSlotAdjacencyAdvisory? advisory)
    {
        if (advisory is null)
        {
            return;
        }

        var conditionValid = !string.IsNullOrWhiteSpace(advisory.WhenProperty)
            && block.Properties.TryGetValue(advisory.WhenProperty, out var conditionProperty)
            && string.Equals(conditionProperty.Type, "string", StringComparison.Ordinal)
            && advisory.WhenValues is { Length: >= 1 and <= 16 }
            && advisory.WhenValues.All(value => !string.IsNullOrWhiteSpace(value))
            && advisory.WhenValues.Distinct(StringComparer.Ordinal).Count() == advisory.WhenValues.Length;
        var itemSpacingValid = !string.IsNullOrWhiteSpace(advisory.ItemSpacingProperty)
            && block.Properties.TryGetValue(advisory.ItemSpacingProperty, out var itemSpacingProperty)
            && string.Equals(itemSpacingProperty.Type, "string", StringComparison.Ordinal)
            && string.Equals(itemSpacingProperty.Format, "thickness", StringComparison.Ordinal);
        var childMarginValid = string.IsNullOrEmpty(advisory.ChildMarginProperty)
            || !string.IsNullOrWhiteSpace(advisory.ChildMarginProperty);
        if (IsSafeSemanticToken(advisory.ChildRole)
            && advisory.UnknownFields is null or { Count: 0 }
            && conditionValid
            && itemSpacingValid
            && childMarginValid
            && advisory.Message is { Length: >= 1 and <= 512 }
            && !string.IsNullOrWhiteSpace(advisory.Message)
            && advisory.RepairSuggestion is { Length: >= 1 and <= 512 }
            && !string.IsNullOrWhiteSpace(advisory.RepairSuggestion))
        {
            return;
        }

        throw new InvalidDataException(
            $"InvalidAdjacencyAdvisory: block '{block.Kind}' slot '{slotName}' must reference declared condition and thickness spacing properties with bounded inert guidance; condition='{advisory.WhenProperty}', spacing='{advisory.ItemSpacingProperty}'.");
    }

    private static bool IsSafeSemanticToken(string value)
        => value is { Length: >= 1 and <= 64 }
           && IsAsciiLetter(value[0])
           && value.Skip(1).All(character => IsAsciiLetter(character)
               || character is >= '0' and <= '9'
               || character is '-' or '_' or '.');

    private static string CreateFingerprint(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in EnumeratePackFiles(root))
        {
            RejectReparsePoint(root, file);
            var fileInfo = new FileInfo(file);
            AppendLengthPrefixed(hash, Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/')));
            AppendInt64(hash, fileInfo.Length);
            using var stream = fileInfo.OpenRead();
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void RejectReparsePoint(string root, string candidate)
    {
        if (ProjectWritePolicy.FindReparsePoint(root, candidate) is { } reparsePoint)
        {
            throw new InvalidDataException(
                $"Pack content path uses a reparse point and cannot be loaded: {Path.GetRelativePath(root, reparsePoint)}.");
        }
    }

    private static IEnumerable<string> EnumeratePackFiles(string root)
        => new[]
        {
            Path.Combine(root, "install.manifest.json"),
            Path.Combine(root, "pack.json"),
            Path.Combine(root, "source.lock.json")
        }.Where(File.Exists)
            .Concat(EnumerateOptionalFiles(Path.Combine(root, "blocks"), "*.block.json"))
            .Concat(EnumerateOptionalFiles(Path.Combine(root, "recipes"), "*.recipe.json"))
            .Concat(EnumerateOptionalFiles(Path.Combine(root, "examples"), "*.ui.json"))
            .Concat(EnumerateOptionalFiles(Path.Combine(root, "vocabularies"), "*.json"))
            .Concat(EnumerateOptionalFiles(Path.Combine(root, "renderers", "xaml"), "*.xaml.sbn"))
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal);

    private static IEnumerable<string> EnumerateOptionalFiles(string directory, string pattern)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            : [];

    private static void AppendLengthPrefixed(IncrementalHash hash, byte[] value)
    {
        AppendInt64(hash, value.LongLength);
        hash.AppendData(value);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
        => hash.AppendData(BitConverter.GetBytes(value));

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

        var pathParts = template.Split('/');
        if (template.Contains('\\', StringComparison.Ordinal)
            || !template.StartsWith("renderers/xaml/", StringComparison.Ordinal)
            || !template.EndsWith(".xaml.sbn", StringComparison.Ordinal)
            || pathParts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."))
        {
            throw new InvalidDataException(
                $"Renderer template for block '{block.Kind}' must use the canonical renderers/xaml/**/*.xaml.sbn contract.");
        }

        if (!File.Exists(fullTemplatePath))
        {
            throw new InvalidDataException(
                $"Renderer template for block '{block.Kind}' is missing: {template}.");
        }
    }

    private static void ValidateCodeBehindBaseType(UiBlockDefinition block)
    {
        var baseType = block.Renderer.CodeBehindBaseType;
        if (string.IsNullOrWhiteSpace(baseType))
        {
            return;
        }

        var parts = baseType.Split('.');
        var valid = parts.Length > 1 && parts.All(part => part.Length > 0
            && (char.IsLetter(part[0]) || part[0] == '_')
            && part.Skip(1).All(ch => char.IsLetterOrDigit(ch) || ch == '_'));
        if (!valid)
        {
            throw new InvalidDataException(
                $"Renderer codeBehindBaseType for block '{block.Kind}' must be a namespace-qualified CLR type name.");
        }
    }

    private static void ValidateRendererNameScopeElements(UiBlockDefinition block)
    {
        if (!block.Renderer.HasNameScopeElementsDeclaration)
        {
            return;
        }

        var elements = block.Renderer.NameScopeElements;
        var invalidElement = elements.FirstOrDefault(element =>
            string.IsNullOrEmpty(element)
            || !(IsAsciiLetter(element[0]) || element[0] == '_')
            || element.Skip(1).Any(ch => !IsAsciiLetter(ch) && ch is not (>= '0' and <= '9') && ch != '_'));
        var hasDuplicates = elements.Distinct(StringComparer.Ordinal).Count() != elements.Length;
        if (elements.Length is >= 1 and <= 64 && invalidElement is null && !hasDuplicates)
        {
            return;
        }

        throw new InvalidDataException(
            $"InvalidRendererNameScopeElements: renderer nameScopeElements for block '{block.Kind}' " +
            "must contain at most 64 unique XAML local names matching [A-Za-z_][A-Za-z0-9_]*.");
    }

    private static bool IsAsciiLetter(char value)
        => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static void ValidateInteractionContract(UiBlockDefinition block)
    {
        var interaction = block.Interaction;
        if (interaction is null)
        {
            return;
        }

        if (interaction.Kind is not "action" and not "navigation")
        {
            throw new InvalidDataException($"Interaction kind for block '{block.Kind}' must be action or navigation.");
        }

        ValidateInteractionProperty(block, "commandProperty", interaction.CommandProperty, required: true);
        ValidateInteractionProperty(block, "commandParameterProperty", interaction.CommandParameterProperty, required: false);
        ValidateInteractionProperty(block, "targetProperty", interaction.TargetProperty, required: false);
        ValidateInteractionProperty(block, "labelProperty", interaction.LabelProperty, required: false);
    }

    private static void ValidateInteractionProperty(
        UiBlockDefinition block,
        string contractName,
        string propertyName,
        bool required)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            if (required)
            {
                throw new InvalidDataException($"Interaction {contractName} for block '{block.Kind}' is required.");
            }

            return;
        }

        if (!block.Properties.ContainsKey(propertyName))
        {
            throw new InvalidDataException(
                $"Interaction {contractName} '{propertyName}' for block '{block.Kind}' must reference a declared property.");
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

internal sealed record ComposerPackLoadResult(ComposerPack Pack, string Fingerprint, bool FromCache);

internal sealed record CachedComposerPack(string Fingerprint, ComposerPack Pack);

internal sealed record ComposerPack(
    UiPackManifest Manifest,
    SourceLock SourceLock,
    IReadOnlyList<UiBlockDefinition> Blocks,
    IReadOnlyList<UiRecipeDefinition> Recipes,
    IReadOnlyList<UiBlueprint> Examples,
    IReadOnlyList<string> RendererTemplates);
