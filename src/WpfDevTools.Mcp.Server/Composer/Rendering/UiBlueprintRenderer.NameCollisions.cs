using System.Xml;
using System.Xml.Linq;
using WpfDevTools.Mcp.Server.Composer.Blueprints;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer
{
    private const string WpfPresentationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly HashSet<string> WpfNameScopeElements =
    [
        "ControlTemplate",
        "DataTemplate",
        "HierarchicalDataTemplate",
        "ItemsPanelTemplate",
        "Style"
    ];

    private static void AddRenderedNameCollisionIssues(
        string rendererXaml,
        string namespacedXaml,
        IReadOnlyList<RenderSourceMapEntry> sourceMap,
        IReadOnlyDictionary<string, Contracts.UiBlockDefinition> blocks,
        List<BlueprintValidationIssue> issues)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(
                namespacedXaml,
                LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            return;
        }

        if (document.Root is null)
        {
            return;
        }

        var insertion = FindNamespaceInsertion(rendererXaml, namespacedXaml);
        VisitNameScope(
            document.Root,
            new Dictionary<string, RenderedNameOccurrence>(StringComparer.Ordinal),
            rendererXaml,
            namespacedXaml,
            insertion,
            sourceMap,
            blocks,
            issues);
    }

    private static void VisitNameScope(
        XElement element,
        Dictionary<string, RenderedNameOccurrence> scope,
        string rendererXaml,
        string namespacedXaml,
        NamespaceInsertion insertion,
        IReadOnlyList<RenderSourceMapEntry> sourceMap,
        IReadOnlyDictionary<string, Contracts.UiBlockDefinition> blocks,
        List<BlueprintValidationIssue> issues)
    {
        foreach (var attribute in element.Attributes().Where(attribute => attribute.Name.LocalName == "Name"))
        {
            var occurrence = CreateOccurrence(attribute, rendererXaml, namespacedXaml, insertion, sourceMap);
            if (scope.TryGetValue(attribute.Value, out var first))
            {
                issues.Add(Issue(
                    occurrence.JsonPath,
                    "RenderedNameCollision",
                    $"Rendered XAML name '{attribute.Value}' is repeated in one namescope by '{first.JsonPath}' and '{occurrence.JsonPath}'.",
                    "Use unique names per renderer instance, or move the named part into a template or style namescope.")
                    with
                    {
                        RelatedJsonPaths = [first.JsonPath, occurrence.JsonPath]
                    });
            }
            else
            {
                scope[attribute.Value] = occurrence;
            }
        }

        var childScope = CreatesNestedNameScope(
            element,
            rendererXaml,
            namespacedXaml,
            insertion,
            sourceMap,
            blocks)
            ? new Dictionary<string, RenderedNameOccurrence>(StringComparer.Ordinal)
            : scope;
        foreach (var child in element.Elements())
        {
            VisitNameScope(
                child,
                childScope,
                rendererXaml,
                namespacedXaml,
                insertion,
                sourceMap,
                blocks,
                issues);
        }
    }

    private static RenderedNameOccurrence CreateOccurrence(
        XAttribute attribute,
        string rendererXaml,
        string namespacedXaml,
        NamespaceInsertion insertion,
        IReadOnlyList<RenderSourceMapEntry> sourceMap)
    {
        var entry = FindSourceEntry(attribute, rendererXaml, namespacedXaml, insertion, sourceMap);
        return new RenderedNameOccurrence(entry?.JsonPath ?? "$.layout");
    }

    private static RenderSourceMapEntry? FindSourceEntry(
        XObject node,
        string rendererXaml,
        string namespacedXaml,
        NamespaceInsertion insertion,
        IReadOnlyList<RenderSourceMapEntry> sourceMap)
    {
        var lineInfo = (IXmlLineInfo)node;
        var namespacedIndex = lineInfo.HasLineInfo()
            ? ToTextIndex(namespacedXaml, lineInfo.LineNumber, lineInfo.LinePosition)
            : -1;
        var rendererIndex = namespacedIndex > insertion.Index
            ? namespacedIndex - insertion.Length
            : namespacedIndex;
        return sourceMap
            .Where(entry => entry.StartIndex <= rendererIndex && rendererIndex < entry.EndIndex)
            .OrderByDescending(entry => entry.JsonPath.Length)
            .FirstOrDefault();
    }

    private static bool CreatesNestedNameScope(
        XElement element,
        string rendererXaml,
        string namespacedXaml,
        NamespaceInsertion insertion,
        IReadOnlyList<RenderSourceMapEntry> sourceMap,
        IReadOnlyDictionary<string, Contracts.UiBlockDefinition> blocks)
    {
        var localName = element.Name.LocalName;
        if (string.Equals(element.Name.NamespaceName, WpfPresentationNamespace, StringComparison.Ordinal)
            && WpfNameScopeElements.Contains(localName))
        {
            return true;
        }

        var sourceEntry = FindSourceEntry(element, rendererXaml, namespacedXaml, insertion, sourceMap);
        return sourceEntry is not null
            && blocks.TryGetValue(sourceEntry.BlockKind, out var block)
            && block.Renderer.NameScopeElements.Contains(localName, StringComparer.Ordinal);
    }

    private static NamespaceInsertion FindNamespaceInsertion(string rendererXaml, string namespacedXaml)
    {
        var length = namespacedXaml.Length - rendererXaml.Length;
        if (length <= 0)
        {
            return new NamespaceInsertion(-1, 0);
        }

        var index = 0;
        while (index < rendererXaml.Length
               && index < namespacedXaml.Length
               && rendererXaml[index] == namespacedXaml[index])
        {
            index++;
        }
        return new NamespaceInsertion(index, length);
    }

    private static int ToTextIndex(string text, int lineNumber, int linePosition)
    {
        var line = 1;
        var index = 0;
        while (index < text.Length && line < lineNumber)
        {
            if (text[index++] == '\n')
            {
                line++;
            }
        }
        return Math.Min(text.Length, index + Math.Max(0, linePosition - 1));
    }

    private sealed record RenderedNameOccurrence(string JsonPath);
    private sealed record NamespaceInsertion(int Index, int Length);
}
