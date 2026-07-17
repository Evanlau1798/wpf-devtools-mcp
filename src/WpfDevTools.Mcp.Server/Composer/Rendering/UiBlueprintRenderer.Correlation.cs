using System.Net;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer
{
    private const string IdentityAttributesToken = "identity.attributes";

    private static readonly Regex RootNamePattern = new(
        "(?:^|\\s)(?:x:Name|Name)\\s*=\\s*(?:\"(?<double>[^\"]+)\"|'(?<single>[^']+)')",
        RegexOptions.CultureInvariant);
    private static readonly Regex RootAutomationIdPattern = new(
        "(?:^|\\s)AutomationProperties\\.AutomationId\\s*=\\s*(?:\"(?<double>[^\"]+)\"|'(?<single>[^']+)')",
        RegexOptions.CultureInvariant);

    private static string AddAuthoredIdentity(
        string xaml,
        UiBlueprintNode node,
        string jsonPath,
        List<BlueprintValidationIssue> errors)
    {
        if (node.ElementName is null && node.AutomationId is null)
        {
            return xaml;
        }

        var rootStart = XamlDocumentRootLocator.FindStart(xaml);
        var rootEnd = rootStart < 0 ? -1 : FindTagEnd(xaml, rootStart + 1);
        if (rootEnd < 0)
        {
            errors.Add(Issue(jsonPath, "AuthoredIdentityRootMissing", "Renderer output has no XAML root for authored identity.", "Repair the pack renderer template before applying this blueprint."));
            return xaml;
        }

        var rootTag = xaml[rootStart..rootEnd];
        var attributes = new List<string>();
        AddAuthoredAttribute(
            node.ElementName,
            "elementName",
            "x:Name",
            RootNamePattern,
            rootTag,
            jsonPath,
            attributes,
            errors);
        AddAuthoredAttribute(
            node.AutomationId,
            "automationId",
            "AutomationProperties.AutomationId",
            RootAutomationIdPattern,
            rootTag,
            jsonPath,
            attributes,
            errors);
        if (attributes.Count == 0)
        {
            return xaml;
        }

        var insertAt = rootEnd > rootStart && xaml[rootEnd - 1] == '/' ? rootEnd - 1 : rootEnd;
        return xaml[..insertAt] + " " + string.Join(" ", attributes) + xaml[insertAt..];
    }

    private static void AddAuthoredAttribute(
        string? authoredValue,
        string field,
        string attribute,
        Regex pattern,
        string rootTag,
        string jsonPath,
        List<string> attributes,
        List<BlueprintValidationIssue> errors)
    {
        if (authoredValue is null)
        {
            return;
        }

        var existing = pattern.Match(rootTag);
        if (!existing.Success)
        {
            attributes.Add($"{attribute}=\"{EscapeAttribute(authoredValue)}\"");
            return;
        }

        var existingValue = WebUtility.HtmlDecode(existing.Groups["double"].Success
            ? existing.Groups["double"].Value
            : existing.Groups["single"].Value);
        if (!string.Equals(existingValue, authoredValue, StringComparison.Ordinal))
        {
            errors.Add(Issue(
                $"{jsonPath}.{field}",
                "AuthoredIdentityRendererConflict",
                $"Authored {field} '{authoredValue}' conflicts with renderer root value '{existingValue}'.",
                $"Use '{existingValue}', omit {field}, or update the pack renderer contract."));
        }
    }

    private static string AddTransientElementCorrelation(
        string xaml,
        string jsonPath,
        string blockKind,
        IReadOnlySet<string> reservedNames,
        List<RenderElementCorrelation> correlations)
    {
        var rootStart = XamlDocumentRootLocator.FindStart(xaml);
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

    private static string RenderIdentityAttributes(
        UiBlueprintNode node,
        string jsonPath,
        string blockKind,
        bool includeTransientElementCorrelation,
        IReadOnlySet<string> reservedNames,
        List<RenderElementCorrelation> correlations)
    {
        var elementName = node.ElementName;
        if (includeTransientElementCorrelation && elementName is null)
        {
            elementName = CreateGeneratedName(reservedNames, correlations);
        }

        var attributes = new List<string>();
        if (elementName is not null)
        {
            attributes.Add($"x:Name=\"{EscapeAttribute(elementName)}\"");
        }
        if (node.AutomationId is not null)
        {
            attributes.Add($"AutomationProperties.AutomationId=\"{EscapeAttribute(node.AutomationId)}\"");
        }
        if (includeTransientElementCorrelation && elementName is not null)
        {
            correlations.Add(new RenderElementCorrelation(elementName, jsonPath, blockKind));
        }

        return attributes.Count == 0 ? string.Empty : " " + string.Join(" ", attributes);
    }

    private void ReserveExistingElementNames(
        UiBlueprintNode root,
        IReadOnlyList<ComposerPackReference> packs,
        IReadOnlyDictionary<string, string> expectedPackFingerprints,
        HashSet<string> reservedNames)
    {
        var pending = new Stack<UiBlueprintNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node.ElementName is not null)
            {
                reservedNames.Add(node.ElementName);
            }

            var template = _templateLoader.Load(node.Kind, packs, expectedPackFingerprints).Template?.Content;
            if (template is not null)
            {
                foreach (var name in GetElementNames(template))
                {
                    reservedNames.Add(name);
                }
            }

            foreach (var child in node.Slots.Values.SelectMany(children => children))
            {
                pending.Push(child);
            }
        }
    }

    private static IEnumerable<string> GetElementNames(string xaml)
    {
        foreach (Match match in RootNamePattern.Matches(xaml))
        {
            yield return WebUtility.HtmlDecode(match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Value);
        }
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
