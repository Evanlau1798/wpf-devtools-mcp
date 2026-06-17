using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolOutputSchemaRecoveryProjectionTests
{
    private static readonly string[] SharedRecoveryProjectionFields =
    [
        "toolName",
        "requiresReconnect",
        "stateAfterTimeoutUnknown",
        "processId",
        "timeoutSeconds",
        "retryAfterSeconds",
        "retryAfter",
        "availableTokens",
        "availableEvents"
    ];

    [Fact]
    public void ExactSchemas_ShouldExposeSharedRecoveryProjectionFields()
    {
        var missingByTool = new List<string>();

        foreach (var toolName in GetHighValueToolNames())
        {
            var tool = new Tool
            {
                Name = toolName,
                InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
            };
            McpToolOutputSchemas.Apply(tool);

            var schema = tool.OutputSchema!.Value;
            if (schema.GetProperty("additionalProperties").GetBoolean())
            {
                continue;
            }

            var properties = schema.GetProperty("properties");
            var missing = SharedRecoveryProjectionFields
                .Where(field => !properties.TryGetProperty(field, out _))
                .ToArray();
            if (missing.Length > 0)
            {
                missingByTool.Add($"{toolName}: {string.Join(", ", missing)}");
            }
        }

        missingByTool.Should().BeEmpty(
            "closed tools/list output schemas must admit timeout and recovery fields projected by ToolCallHelper");
    }

    private static string[] GetHighValueToolNames()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        return document.RootElement.GetProperty("highValueTools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("tool").GetString()!)
            .ToArray();
    }
}
