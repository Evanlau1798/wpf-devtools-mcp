using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer(PackRegistry registry)
{
    private static readonly Regex TokenPattern = new(
        @"\{\{\s*(?<name>[A-Za-z0-9_.-]+)\s*\}\}",
        RegexOptions.CultureInvariant);

    private static readonly Regex EmptyPropertyElementPattern = new(
        @"\s*<(?<prefix>[A-Za-z_][A-Za-z0-9_]*):(?<type>[A-Za-z_][A-Za-z0-9_]*)\.(?<property>[A-Za-z_][A-Za-z0-9_]*)>\s*</\k<prefix>:\k<type>\.\k<property>>",
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
        var sourceMap = new List<RenderSourceMapEntry>();
        var rendererXaml = RenderNode(blueprint.Layout, "$.layout", blueprint.Packs, context, errors, sourceMap);
        var resolvedSourceMap = ResolveSourceMap(rendererXaml, sourceMap);
        errors.AddRange(XamlSafetyScanner.Scan(rendererXaml, resolvedSourceMap, context.RequiredResources));
        var xaml = AddRootXmlNamespaces(rendererXaml, context.XmlNamespaces);
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
            context.Diagnostics,
            resolvedSourceMap);
    }

    private string RenderNode(
        UiBlueprintNode node,
        string path,
        IReadOnlyList<ComposerPackReference> packs,
        RenderContext context,
        List<BlueprintValidationIssue> errors,
        List<RenderSourceMapEntry> sourceMap)
    {
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

        var template = OmitUnsetPropertyAttributes(templateResult.Template.Content, node, block);
        var rendered = TokenPattern.Replace(template, match =>
            ResolveToken(match.Groups["name"].Value, node, block, path, packs, context, errors, sourceMap));
        rendered = EmptyPropertyElementPattern.Replace(rendered, string.Empty);
        sourceMap.Add(new RenderSourceMapEntry(path, node.Kind, templateResult.Template.TemplatePath, rendered));
        return rendered;
    }

    private string ResolveToken(
        string token,
        UiBlueprintNode node,
        UiBlockDefinition block,
        string path,
        IReadOnlyList<ComposerPackReference> packs,
        RenderContext context,
        List<BlueprintValidationIssue> errors,
        List<RenderSourceMapEntry> sourceMap)
    {
        if (token.StartsWith("slot.", StringComparison.Ordinal))
        {
            var slotName = token["slot.".Length..];
            if (!block.Slots.ContainsKey(slotName))
            {
                errors.Add(Issue(path, "RendererTokenMismatch", $"Renderer token '{token}' does not match a slot on block '{block.Kind}'.", "Update the renderer template token or add the slot to the block contract."));
                return string.Empty;
            }

            if (!node.Slots.TryGetValue(slotName, out var children) || children.Length == 0)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine, children.Select((child, index) =>
            {
                var childXaml = RenderNode(child, $"{path}.slots.{slotName}[{index}]", packs, context, errors, sourceMap);
                return WrapSlotItem(block.Slots[slotName], childXaml, node, block, path, errors);
            }));
        }

        if (!block.Properties.ContainsKey(token))
        {
            errors.Add(Issue(path, "RendererTokenMismatch", $"Renderer token '{token}' does not match a property on block '{block.Kind}'.", "Update the renderer template token or add the property to the block contract."));
            return string.Empty;
        }

        var value = GetPropertyValue(node, token)
            ?? GetDefaultPropertyValue(block, token)
            ?? string.Empty;
        return Escape(value);
    }

    private static string WrapSlotItem(UiBlockSlot slot, string childXaml, UiBlueprintNode node,
        UiBlockDefinition block, string path, List<BlueprintValidationIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(slot.XamlItemTemplate))
        {
            return childXaml;
        }

        var itemTemplate = OmitUnsetPropertyAttributes(slot.XamlItemTemplate, node, block);
        return TokenPattern.Replace(itemTemplate, match =>
        {
            var token = match.Groups["name"].Value;
            if (token == "item")
            {
                return childXaml;
            }

            if (block.Properties.ContainsKey(token))
            {
                return Escape(GetPropertyValue(node, token) ?? GetDefaultPropertyValue(block, token) ?? string.Empty);
            }

            errors.Add(Issue(path, "RendererTokenMismatch", $"Slot item template token '{token}' does not match block '{block.Kind}'.", "Use {{item}} or a declared block property in xamlItemTemplate."));
            return string.Empty;
        });
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

    private static string AddRootXmlNamespaces(string xaml, IReadOnlyDictionary<string, string> xmlNamespaces)
    {
        var rootStart = FindRootElementStart(xaml);
        if (rootStart < 0)
        {
            return xaml;
        }

        var rootEnd = FindTagEnd(xaml, rootStart + 1);
        if (rootEnd < 0)
        {
            return xaml;
        }

        var rootTag = xaml[rootStart..rootEnd];
        var attributes = new List<string>();
        if (!HasRootAttribute(rootTag, "xmlns"))
        {
            attributes.Add("xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
        }

        if (!HasRootAttribute(rootTag, "xmlns:x"))
        {
            attributes.Add("xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
        }

        foreach (var xmlNamespace in xmlNamespaces.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (xaml.Contains(xmlNamespace.Key + ":", StringComparison.Ordinal)
                && !HasRootAttribute(rootTag, "xmlns:" + xmlNamespace.Key))
            {
                attributes.Add($"xmlns:{xmlNamespace.Key}=\"{EscapeAttribute(xmlNamespace.Value)}\"");
            }
        }

        if (attributes.Count == 0)
        {
            return xaml;
        }

        var insertAt = rootEnd > rootStart && xaml[rootEnd - 1] == '/' ? rootEnd - 1 : rootEnd;
        return xaml[..insertAt] + " " + string.Join(" ", attributes) + xaml[insertAt..];
    }

    private static int FindRootElementStart(string xaml)
    {
        var index = 0;
        while (index < xaml.Length)
        {
            var start = xaml.IndexOf('<', index);
            if (start < 0 || start + 1 >= xaml.Length)
            {
                return -1;
            }

            if (xaml[start + 1] is not '/' and not '!' and not '?')
            {
                return start;
            }

            index = start + 1;
        }

        return -1;
    }

    private static int FindTagEnd(string xaml, int start)
    {
        var quote = '\0';
        for (var index = start; index < xaml.Length; index++)
        {
            if (quote != '\0')
            {
                if (xaml[index] == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (xaml[index] is '"' or '\'')
            {
                quote = xaml[index];
            }
            else if (xaml[index] == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool HasRootAttribute(string rootTag, string name)
        => rootTag.Contains(" " + name + "=", StringComparison.Ordinal)
            || rootTag.Contains("\t" + name + "=", StringComparison.Ordinal)
            || rootTag.Contains("\r" + name + "=", StringComparison.Ordinal)
            || rootTag.Contains("\n" + name + "=", StringComparison.Ordinal);

    private static string EscapeAttribute(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string ResolveTargetPath(RenderBlueprintRequest request, UiBlueprint blueprint)
        => RenderTargetPath.Resolve(request, string.IsNullOrWhiteSpace(request.TargetPath)
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

    private static IReadOnlyList<RenderSourceMapEntry> ResolveSourceMap(
        string xaml,
        IReadOnlyList<RenderSourceMapEntry> entries)
    {
        var resolved = new List<RenderSourceMapEntry>();
        var ordered = entries.OrderBy(entry => entry.JsonPath.Length).ToArray();
        foreach (var entry in ordered)
        {
            var parent = resolved
                .Where(candidate => IsChildPath(entry.JsonPath, candidate.JsonPath))
                .OrderByDescending(candidate => candidate.JsonPath.Length)
                .FirstOrDefault();
            var start = parent is null ? 0 : parent.StartIndex;
            var length = parent is null ? xaml.Length : Math.Max(0, parent.EndIndex - parent.StartIndex);
            var index = FindSpanStart(xaml, entry.Xaml, start, length);
            if (index < 0)
            {
                continue;
            }

            var end = index + entry.Xaml.Length;
            var startPosition = ToLineColumn(xaml, index);
            var endPosition = ToLineColumn(xaml, Math.Max(index, end - 1));
            resolved.Add(entry with
            {
                StartIndex = index,
                EndIndex = end,
                StartLine = startPosition.Line,
                StartColumn = startPosition.Column,
                EndLine = endPosition.Line,
                EndColumn = endPosition.Column
            });
        }

        return resolved.OrderBy(entry => entry.StartIndex).ThenBy(entry => entry.JsonPath.Length).ToArray();
    }

    private static int FindSpanStart(string haystack, string needle, int startIndex, int length)
    {
        if (string.IsNullOrEmpty(needle) || startIndex < 0 || startIndex >= haystack.Length)
        {
            return -1;
        }

        return haystack.IndexOf(needle, startIndex, Math.Min(length, haystack.Length - startIndex), StringComparison.Ordinal);
    }

    private static bool IsChildPath(string path, string parentPath)
        => path.Length > parentPath.Length
            && path.StartsWith(parentPath + ".slots.", StringComparison.Ordinal);

    private static (int Line, int Column) ToLineColumn(string text, int index)
    {
        var line = 1;
        var column = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                line++;
                column = 1;
                if (i + 1 < index && text[i + 1] == '\n')
                {
                    i++;
                }
            }
            else if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private sealed class RenderContext
    {
        private RenderContext(
            IReadOnlyDictionary<string, UiBlockDefinition> blocks,
            IReadOnlyList<RequiredNuGetPackage> packages,
            IReadOnlyList<string> resources,
            IReadOnlyDictionary<string, string> xmlNamespaces,
            IReadOnlyList<string> diagnostics)
        {
            Blocks = blocks;
            RequiredNuGetPackages = packages;
            RequiredResources = resources;
            XmlNamespaces = xmlNamespaces;
            Diagnostics = diagnostics;
        }

        public IReadOnlyDictionary<string, UiBlockDefinition> Blocks { get; }
        public IReadOnlyList<RequiredNuGetPackage> RequiredNuGetPackages { get; }
        public IReadOnlyList<string> RequiredResources { get; }
        public IReadOnlyDictionary<string, string> XmlNamespaces { get; }
        public IReadOnlyList<string> Diagnostics { get; }

        public static RenderContext Create(PackRegistry registry, IReadOnlyList<ComposerPackReference> declaredPacks)
        {
            var registryResult = registry.ListPacks();
            var registryById = registryResult.Packs.ToDictionary(pack => pack.Id, StringComparer.Ordinal);
            var blocks = new Dictionary<string, UiBlockDefinition>(StringComparer.Ordinal);
            var packages = new List<RequiredNuGetPackage>();
            var resources = new List<string>();
            var xmlNamespaces = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var declared in declaredPacks)
            {
                if (!registryById.TryGetValue(declared.Id, out var pack)
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
                foreach (var xmlNamespace in loaded.Manifest.XmlNamespaces)
                {
                    xmlNamespaces.TryAdd(xmlNamespace.Key, xmlNamespace.Value);
                }
            }

            return new RenderContext(
                blocks,
                packages.Distinct().OrderBy(package => package.Id, StringComparer.Ordinal).ToArray(),
                resources.Distinct(StringComparer.Ordinal).ToArray(),
                xmlNamespaces,
                registryResult.Diagnostics);
        }
    }
}

internal sealed record RenderBlueprintRequest(string BlueprintJson, string? TargetPath = null, string? ProjectRoot = null);

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
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<RenderSourceMapEntry> SourceMap)
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
            FilePlan: new RenderFilePlan(RenderTargetPath.Resolve(request, request.TargetPath ?? Path.Combine("Views", "GeneratedView.xaml")), WouldWriteFiles: false),
            RequiredResources: [],
            RequiredNuGetPackages: [],
            Validation: validation,
            Errors: errors,
            Diagnostics: validation.Diagnostics,
            SourceMap: []);
}

internal sealed record RenderFilePlan(string TargetPath, bool WouldWriteFiles);

internal static class RenderTargetPath
{
    public static string Resolve(RenderBlueprintRequest request, string targetPath)
        => Path.GetFullPath(!Path.IsPathFullyQualified(targetPath) && !string.IsNullOrWhiteSpace(request.ProjectRoot)
            ? Path.Combine(request.ProjectRoot, targetPath)
            : targetPath);
}

internal sealed record RequiredNuGetPackage(string Id, string VersionRange);

internal sealed record RenderSourceMapEntry(
    string JsonPath,
    string BlockKind,
    string RendererTemplatePath,
    string Xaml,
    int StartIndex = -1,
    int EndIndex = -1,
    int StartLine = 0,
    int StartColumn = 0,
    int EndLine = 0,
    int EndColumn = 0);
