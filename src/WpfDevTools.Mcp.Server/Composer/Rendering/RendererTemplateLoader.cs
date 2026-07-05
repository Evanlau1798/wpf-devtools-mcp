using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed class RendererTemplateLoader(PackRegistry registry)
{
    private static readonly Regex TokenPattern = new(
        @"\{\{\s*(?<name>[A-Za-z0-9_.-]+)\s*\}\}",
        RegexOptions.CultureInvariant);

    private readonly Dictionary<string, RendererTemplate> _cache = new(StringComparer.Ordinal);

    public RendererTemplateLoadResult Load(string blockKind, IReadOnlyList<ComposerPackReference> declaredPacks)
    {
        var errors = new List<BlueprintValidationIssue>();
        var registryResult = registry.ListPacks();
        var packId = GetPackId(blockKind);
        var packRef = declaredPacks.FirstOrDefault(reference =>
            string.Equals(reference.Id, packId, StringComparison.Ordinal));
        if (packRef is null)
        {
            errors.Add(Issue("$", "PackNotDeclared", $"Block kind '{blockKind}' uses undeclared pack '{packId}'.", $"Add pack '{packId}' to blueprint packs[]."));
            return new RendererTemplateLoadResult(false, null, errors, false);
        }

        var pack = registryResult.Packs.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, packRef.Id, StringComparison.Ordinal)
            && string.Equals(candidate.Version, packRef.Version, StringComparison.Ordinal));
        if (pack is null)
        {
            errors.Add(Issue("$", "PackNotFound", $"Pack '{packRef.Id}' {packRef.Version} is not installed.", "Install the required pack before rendering."));
            return new RendererTemplateLoadResult(false, null, errors, false);
        }

        var cacheKey = $"{pack.Id}|{pack.Version}|{blockKind}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return new RendererTemplateLoadResult(true, cached, [], true);
        }

        var loadedPack = ComposerPackLoader.Load(pack.RootPath);
        var block = loadedPack.Blocks.FirstOrDefault(candidate =>
            string.Equals(candidate.Kind, blockKind, StringComparison.Ordinal));
        if (block is null)
        {
            errors.Add(Issue("$", "UnknownBlockKind", $"Block kind '{blockKind}' was not found in pack '{pack.Id}'.", "Refresh the catalog and use a listed block kind."));
            return new RendererTemplateLoadResult(false, null, errors, false);
        }

        var templatePath = ResolveTemplatePath(pack.RootPath, block.Renderer.XamlTemplate);
        if (!IsUnderRoot(pack.RootPath, templatePath) || !File.Exists(templatePath))
        {
            errors.Add(Issue("$", "RendererTemplateMissing", $"Renderer template for block '{blockKind}' was not found: {block.Renderer.XamlTemplate}.", "Repair the pack renderer template path before rendering."));
            return new RendererTemplateLoadResult(false, null, errors, false);
        }

        var content = File.ReadAllText(templatePath);
        var template = new RendererTemplate(
            blockKind,
            templatePath,
            content,
            TokenPattern.Matches(content)
                .Select(match => match.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
        _cache[cacheKey] = template;
        return new RendererTemplateLoadResult(true, template, [], false);
    }

    private static string GetPackId(string blockKind)
    {
        var index = blockKind.IndexOf('.', StringComparison.Ordinal);
        return index < 0 ? string.Empty : blockKind[..index];
    }

    private static string ResolveTemplatePath(string packRoot, string template)
        => Path.GetFullPath(Path.Combine(packRoot, template.Replace('/', Path.DirectorySeparatorChar)));

    private static bool IsUnderRoot(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static BlueprintValidationIssue Issue(string jsonPath, string code, string message, string repairSuggestion)
        => new(jsonPath, code, message, repairSuggestion, [], [], null);
}

internal sealed record RendererTemplateLoadResult(
    bool Success,
    RendererTemplate? Template,
    IReadOnlyList<BlueprintValidationIssue> Errors,
    bool FromCache);

internal sealed record RendererTemplate(
    string BlockKind,
    string TemplatePath,
    string Content,
    IReadOnlyList<string> Tokens);
