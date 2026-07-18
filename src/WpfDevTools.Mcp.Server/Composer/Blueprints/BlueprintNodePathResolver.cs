using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static partial class BlueprintNodePathResolver
{
    public static BlueprintNodePathResolution Resolve(JsonObject blueprint, string requestedPath)
    {
        if (!requestedPath.StartsWith('@'))
        {
            return BlueprintNodePathResolution.Resolved(requestedPath, usedAlias: false);
        }

        var match = AliasPattern().Match(requestedPath);
        if (!match.Success)
        {
            return BlueprintNodePathResolution.Invalid(
                "InvalidElementAlias",
                "An element alias must contain a safe elementName followed by a relative property path.",
                "Use @ElementName.slots.content or @ElementName.properties.text.");
        }

        var elementName = match.Groups["name"].Value;
        var matches = EnumerateNodes(blueprint)
            .Where(candidate => HasElementName(candidate.Node, elementName))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            return BlueprintNodePathResolution.Invalid(
                "ElementAliasNotFound",
                $"No blueprint node has elementName '{elementName}'.",
                "Use an elementName from the current blueprint or pass an exact JSON path.");
        }

        if (matches.Length > 1)
        {
            return BlueprintNodePathResolution.Invalid(
                "ElementAliasAmbiguous",
                $"More than one blueprint node has elementName '{elementName}'.",
                "Make elementName values unique before using an element alias.");
        }

        return BlueprintNodePathResolution.Resolved(
            matches[0].Path + match.Groups["suffix"].Value,
            usedAlias: true);
    }

    private static bool HasElementName(JsonObject node, string expected)
        => node["elementName"] is JsonValue value
           && value.TryGetValue<string>(out var actual)
           && string.Equals(actual, expected, StringComparison.Ordinal);

    private static IEnumerable<(JsonObject Node, string Path)> EnumerateNodes(JsonObject blueprint)
    {
        if (blueprint["layout"] is not JsonObject layout)
        {
            yield break;
        }

        var pending = new Stack<(JsonObject Node, string Path)>();
        pending.Push((layout, "$.layout"));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            if (current.Node["slots"] is not JsonObject slots)
            {
                continue;
            }

            foreach (var (slotName, slotValue) in slots.Reverse())
            {
                if (slotValue is not JsonArray children)
                {
                    continue;
                }

                for (var index = children.Count - 1; index >= 0; index--)
                {
                    if (children[index] is JsonObject child)
                    {
                        var slotPath = BlueprintCompositionTargetPath.AppendProperty(
                            current.Path + ".slots",
                            slotName);
                        pending.Push((child, $"{slotPath}[{index}]"));
                    }
                }
            }
        }
    }

    [GeneratedRegex("^@(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<suffix>\\..+)$", RegexOptions.CultureInvariant)]
    private static partial Regex AliasPattern();
}

internal sealed record BlueprintNodePathResolution(
    bool Success,
    string JsonPath,
    bool UsedAlias,
    string? Code,
    string? Message,
    string? RepairSuggestion)
{
    public static BlueprintNodePathResolution Resolved(string path, bool usedAlias)
        => new(true, path, usedAlias, null, null, null);

    public static BlueprintNodePathResolution Invalid(string code, string message, string repairSuggestion)
        => new(false, string.Empty, true, code, message, repairSuggestion);
}
