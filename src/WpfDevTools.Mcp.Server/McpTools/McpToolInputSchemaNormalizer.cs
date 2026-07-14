using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace WpfDevTools.Mcp.Server.McpTools;

internal static class McpToolInputSchemaNormalizer
{
    public static void Apply(IEnumerable<Tool> tools)
    {
        foreach (var tool in tools)
        {
            Apply(tool);
        }
    }

    public static void Apply(Tool tool)
    {
        var schema = JsonNode.Parse(tool.InputSchema.GetRawText()) as JsonObject;
        if (schema?["properties"] is not JsonObject properties)
        {
            return;
        }

        var changed = false;
        foreach (var property in properties)
        {
            if (property.Value is not JsonObject parameter
                || !ContainsType(parameter["type"], "array")
                || parameter["enum"] is not JsonArray allowedValues
                || parameter["items"] is not JsonObject items
                || items.ContainsKey("enum")
                || !ContainsType(items["type"], "string")
                || allowedValues.Count == 0
                || allowedValues.Any(value => value is not JsonValue candidate
                    || !candidate.TryGetValue<string>(out _)))
            {
                continue;
            }

            items["enum"] = allowedValues.DeepClone();
            parameter.Remove("enum");
            changed = true;
        }

        if (changed)
        {
            tool.InputSchema = JsonSerializer.SerializeToElement(schema);
        }
    }

    private static bool ContainsType(JsonNode? typeNode, string expectedType)
        => typeNode is JsonValue value && value.TryGetValue<string>(out var type)
            ? string.Equals(type, expectedType, StringComparison.Ordinal)
            : typeNode is JsonArray types
              && types.Any(item => item is JsonValue candidate
                  && candidate.TryGetValue<string>(out var type)
                  && string.Equals(type, expectedType, StringComparison.Ordinal));
}
