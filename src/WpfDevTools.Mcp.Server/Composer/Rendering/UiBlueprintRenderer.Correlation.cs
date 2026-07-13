using System.Net;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer
{
    private static readonly Regex RootNamePattern = new(
        "(?:^|\\s)(?:x:Name|Name)\\s*=\\s*(?:\"(?<double>[^\"]+)\"|'(?<single>[^']+)')",
        RegexOptions.CultureInvariant);

    private static string AddTransientElementCorrelation(
        string xaml,
        string jsonPath,
        string blockKind,
        IReadOnlySet<string> reservedNames,
        List<RenderElementCorrelation> correlations)
    {
        var rootStart = FindRootElementStart(xaml);
        var rootEnd = rootStart < 0 ? -1 : FindTagEnd(xaml, rootStart + 1);
        if (rootEnd < 0)
        {
            return xaml;
        }

        var rootTag = xaml[rootStart..rootEnd];
        var existingName = RootNamePattern.Match(rootTag);
        var elementName = existingName.Success
            ? WebUtility.HtmlDecode(existingName.Groups["double"].Success
                ? existingName.Groups["double"].Value
                : existingName.Groups["single"].Value)
            : CreateGeneratedName(reservedNames, correlations);
        if (!existingName.Success)
        {
            var insertAt = rootEnd > rootStart && xaml[rootEnd - 1] == '/' ? rootEnd - 1 : rootEnd;
            xaml = xaml[..insertAt] + $" x:Name=\"{elementName}\"" + xaml[insertAt..];
        }

        correlations.Add(new RenderElementCorrelation(elementName, jsonPath, blockKind));
        return xaml;
    }

    private void ReserveExistingElementNames(
        UiBlueprintNode root,
        IReadOnlyList<ComposerPackReference> packs,
        HashSet<string> reservedNames)
    {
        var pending = new Stack<UiBlueprintNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            var template = _templateLoader.Load(node.Kind, packs).Template?.Content;
            if (template is not null && GetRootElementName(template) is { } name)
            {
                reservedNames.Add(name);
            }

            foreach (var child in node.Slots.Values.SelectMany(children => children))
            {
                pending.Push(child);
            }
        }
    }

    private static string? GetRootElementName(string xaml)
    {
        var rootStart = FindRootElementStart(xaml);
        var rootEnd = rootStart < 0 ? -1 : FindTagEnd(xaml, rootStart + 1);
        if (rootEnd < 0)
        {
            return null;
        }

        var match = RootNamePattern.Match(xaml[rootStart..rootEnd]);
        return match.Success
            ? WebUtility.HtmlDecode(match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Value)
            : null;
    }

    private static string CreateGeneratedName(
        IReadOnlySet<string> reservedNames,
        IReadOnlyCollection<RenderElementCorrelation> correlations)
    {
        var usedNames = correlations.Select(item => item.ElementName).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; ; index++)
        {
            var candidate = $"WpfDevToolsBp_{index:D4}";
            if (!reservedNames.Contains(candidate) && !usedNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
