using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed class UiBlueprintRenderer(PackRegistry registry)
{
    private static readonly Regex TokenPattern = new(
        @"\{\{\s*(?<name>[A-Za-z0-9_.-]+)\s*\}\}",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RendererTemplateLoader _templateLoader = new(registry);

    public RenderBlueprintResult Render(RenderBlueprintRequest request)
    {
        var validation = new BlueprintValidationService(registry).Validate(request.BlueprintJson);
        if (!validation.Success)
        {
            return RenderBlueprintResult.Invalid(request, validation, validation.Errors);
        }

        var blueprint = ComposerJsonLoader.Parse<UiBlueprint>(
            request.BlueprintJson,
            "<inline-blueprint>",
            UiComposerSchemaVersions.UiBlueprint);
        var context = RenderContext.Create(registry, blueprint.Packs);
        var errors = new List<BlueprintValidationIssue>();
        var xaml = RenderNode(blueprint.Layout, "$.layout", blueprint.Packs, context, errors);
        var filePlan = new RenderFilePlan(ResolveTargetPath(request, blueprint), WouldWriteFiles: false);

        return new RenderBlueprintResult(
            errors.Count == 0,
            errors.Count == 0,
            DryRun: true,
            xaml,
            filePlan,
            context.RequiredResources,
            context.RequiredNuGetPackages,
            validation,
            errors,
            context.Diagnostics);
    }

    private string RenderNode(
        UiBlueprintNode node,
        string path,
        IReadOnlyList<ComposerPackReference> packs,
        RenderContext context,
        List<BlueprintValidationIssue> errors)
    {
        if (node.Kind == "text")
        {
            return Escape(GetPropertyValue(node, "value") ?? GetPropertyValue(node, "text") ?? string.Empty);
        }

        if (node.Kind == "template")
        {
            return "<!-- template -->";
        }

        if (node.Kind == "stack")
        {
            var children = node.Slots.Values.SelectMany(slot => slot)
                .Select((child, index) => RenderNode(child, $"{path}.slots.stack[{index}]", packs, context, errors));
            return "<StackPanel>" + Environment.NewLine + string.Join(Environment.NewLine, children) + Environment.NewLine + "</StackPanel>";
        }

        if (!context.Blocks.TryGetValue(node.Kind, out var block))
        {
            errors.Add(Issue(path, "UnknownBlockKind", $"Block kind '{node.Kind}' was not loaded.", "Refresh the catalog and choose a loaded block kind."));
            return string.Empty;
        }

        var templateResult = _templateLoader.Load(node.Kind, packs);
        if (!templateResult.Success || templateResult.Template is null)
        {
            errors.AddRange(templateResult.Errors.Select(error => error with { JsonPath = path }));
            return string.Empty;
        }

        return TokenPattern.Replace(templateResult.Template.Content, match =>
            ResolveToken(match.Groups["name"].Value, node, block, path, packs, context, errors));
    }

    private string ResolveToken(
        string token,
        UiBlueprintNode node,
        UiBlockDefinition block,
        string path,
        IReadOnlyList<ComposerPackReference> packs,
        RenderContext context,
        List<BlueprintValidationIssue> errors)
    {
        if (token.StartsWith("slot.", StringComparison.Ordinal))
        {
            var slotName = token["slot.".Length..];
            if (!node.Slots.TryGetValue(slotName, out var children) || children.Length == 0)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine, children.Select((child, index) =>
                RenderNode(child, $"{path}.slots.{slotName}[{index}]", packs, context, errors)));
        }

        var value = GetPropertyValue(node, token)
            ?? GetDefaultPropertyValue(block, token)
            ?? string.Empty;
        return Escape(value);
    }

    private static string? GetPropertyValue(UiBlueprintNode node, string name)
        => node.Properties.TryGetValue(name, out var value) ? JsonElementToText(value) : null;

    private static string? GetDefaultPropertyValue(UiBlockDefinition block, string name)
        => block.Properties.TryGetValue(name, out var property) && property.Default is JsonElement value
            ? JsonElementToText(value)
            : null;

    private static string JsonElementToText(JsonElement value)
        => value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();

    private static string Escape(string value)
        => WebUtility.HtmlEncode(value);

    private static string ResolveTargetPath(RenderBlueprintRequest request, UiBlueprint blueprint)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(request.TargetPath)
            ? Path.Combine("Views", SanitizeFileName(blueprint.Name) + ".xaml")
            : request.TargetPath);

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "GeneratedView" : sanitized;
    }

    private static BlueprintValidationIssue Issue(string jsonPath, string code, string message, string repairSuggestion)
        => new(jsonPath, code, message, repairSuggestion, [], [], null);

    private sealed class RenderContext
    {
        private RenderContext(
            IReadOnlyDictionary<string, UiBlockDefinition> blocks,
            IReadOnlyList<RequiredNuGetPackage> packages,
            IReadOnlyList<string> resources,
            IReadOnlyList<string> diagnostics)
        {
            Blocks = blocks;
            RequiredNuGetPackages = packages;
            RequiredResources = resources;
            Diagnostics = diagnostics;
        }

        public IReadOnlyDictionary<string, UiBlockDefinition> Blocks { get; }
        public IReadOnlyList<RequiredNuGetPackage> RequiredNuGetPackages { get; }
        public IReadOnlyList<string> RequiredResources { get; }
        public IReadOnlyList<string> Diagnostics { get; }

        public static RenderContext Create(PackRegistry registry, IReadOnlyList<ComposerPackReference> declaredPacks)
        {
            var registryResult = registry.ListPacks();
            var declaredById = declaredPacks.ToDictionary(pack => pack.Id, StringComparer.Ordinal);
            var blocks = new Dictionary<string, UiBlockDefinition>(StringComparer.Ordinal);
            var packages = new List<RequiredNuGetPackage>();
            var resources = new List<string>();

            foreach (var pack in registryResult.Packs)
            {
                if (!declaredById.TryGetValue(pack.Id, out var declared)
                    || !string.Equals(pack.Version, declared.Version, StringComparison.Ordinal))
                {
                    continue;
                }

                var loaded = ComposerPackLoader.Load(pack.RootPath);
                foreach (var block in loaded.Blocks)
                {
                    blocks[block.Kind] = block;
                }

                packages.AddRange(loaded.Manifest.NugetPackages.Select(package =>
                    new RequiredNuGetPackage(package.Id, package.VersionRange)));
                resources.AddRange(loaded.Manifest.ResourceSetup.ApplicationMergedDictionaries);
            }

            return new RenderContext(
                blocks,
                packages.Distinct().OrderBy(package => package.Id, StringComparer.Ordinal).ToArray(),
                resources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                registryResult.Diagnostics);
        }
    }
}

internal sealed record RenderBlueprintRequest(string BlueprintJson, string? TargetPath = null);

internal sealed record RenderBlueprintResult(
    bool Success,
    bool Valid,
    bool DryRun,
    string Xaml,
    RenderFilePlan FilePlan,
    IReadOnlyList<string> RequiredResources,
    IReadOnlyList<RequiredNuGetPackage> RequiredNuGetPackages,
    BlueprintValidationResult Validation,
    IReadOnlyList<BlueprintValidationIssue> Errors,
    IReadOnlyList<string> Diagnostics)
{
    public static RenderBlueprintResult Invalid(
        RenderBlueprintRequest request,
        BlueprintValidationResult validation,
        IReadOnlyList<BlueprintValidationIssue> errors)
        => new(
            Success: false,
            Valid: false,
            DryRun: true,
            Xaml: string.Empty,
            FilePlan: new RenderFilePlan(Path.GetFullPath(request.TargetPath ?? Path.Combine("Views", "GeneratedView.xaml")), WouldWriteFiles: false),
            RequiredResources: [],
            RequiredNuGetPackages: [],
            Validation: validation,
            Errors: errors,
            Diagnostics: validation.Diagnostics);
}

internal sealed record RenderFilePlan(string TargetPath, bool WouldWriteFiles);

internal sealed record RequiredNuGetPackage(string Id, string VersionRange);
