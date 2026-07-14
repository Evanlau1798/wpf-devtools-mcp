using System.Xml;
using System.Xml.Linq;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal static class GeneratedClassMemberCollisionValidator
{
    public static IReadOnlyList<BlueprintValidationIssue> Validate(
        UiBlueprint blueprint,
        IReadOnlyDictionary<string, UiBlockDefinition> blocks,
        string targetPath)
    {
        if (!TryResolveGeneratedClassName(blueprint, blocks, targetPath, out var className))
        {
            return [];
        }

        var issues = new List<BlueprintValidationIssue>();
        var pending = new Stack<(UiBlueprintNode Node, string Path)>();
        pending.Push((blueprint.Layout, "$.layout"));

        while (pending.Count > 0)
        {
            var (node, path) = pending.Pop();
            if (string.Equals(node.ElementName, className, StringComparison.Ordinal))
            {
                issues.Add(new BlueprintValidationIssue(
                    path + ".elementName",
                    "GeneratedClassMemberNameCollision",
                    $"Authored elementName '{node.ElementName}' conflicts with generated class '{className}'.",
                    "Choose an elementName that differs from the target XAML filename, or choose a different targetPath.",
                    [],
                    [],
                    null));
            }

            foreach (var slot in node.Slots.OrderByDescending(pair => pair.Key, StringComparer.Ordinal))
            {
                for (var index = slot.Value.Length - 1; index >= 0; index--)
                {
                    pending.Push((slot.Value[index], $"{path}.slots.{slot.Key}[{index}]"));
                }
            }
        }

        return issues;
    }

    public static void AddRenderedIssues(
        UiBlueprint blueprint,
        IReadOnlyDictionary<string, UiBlockDefinition> blocks,
        string targetPath,
        string xaml,
        List<BlueprintValidationIssue> issues)
    {
        if (issues.Any(issue => issue.Code == "GeneratedClassMemberNameCollision")
            || !TryResolveGeneratedClassName(blueprint, blocks, targetPath, out var className))
        {
            return;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return;
        }

        if (document.Root is null
            || !document.Root.DescendantsAndSelf().SelectMany(element => element.Attributes()).Any(attribute =>
                attribute.Name.LocalName == "Name"
                && string.Equals(attribute.Value, className, StringComparison.Ordinal)))
        {
            return;
        }

        issues.Add(new BlueprintValidationIssue(
            "$.layout",
            "GeneratedClassMemberNameCollision",
            $"Rendered XAML name '{className}' conflicts with generated class '{className}'.",
            "Change the renderer name or bound name property, or choose a different targetPath.",
            [],
            [],
            null));
    }

    private static bool TryResolveGeneratedClassName(
        UiBlueprint blueprint,
        IReadOnlyDictionary<string, UiBlockDefinition> blocks,
        string targetPath,
        out string className)
    {
        className = string.Empty;
        if (!blocks.TryGetValue(blueprint.Layout.Kind, out var rootBlock)
            || string.IsNullOrWhiteSpace(rootBlock.Renderer.CodeBehindBaseType))
        {
            return false;
        }

        className = ComposerCSharpIdentifier.Create(
            Path.GetFileNameWithoutExtension(targetPath),
            "MainWindow");
        return true;
    }
}
