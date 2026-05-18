using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace WpfDevTools.Mcp.Server.McpTools;

internal static class McpToolOutputSchemas
{
    private static readonly JsonElement StructuredPayloadSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "success" },
        additionalProperties = true,
        properties = new Dictionary<string, object>
        {
            ["success"] = new { type = "boolean", description = "Whether the tool operation succeeded." },
            ["error"] = new { type = "string", description = "Human-readable error message when success is false." },
            ["errorCode"] = new { type = "string", description = "Machine-readable error code when available." },
            ["message"] = new { type = "string", description = "Human-readable status or summary message." },
            ["processId"] = new { type = "integer", description = "Target or selected WPF process identifier." },
            ["processName"] = new { type = "string", description = "Target process name when available." },
            ["windowTitle"] = new { type = "string", description = "Target WPF window title when available." },
            ["summaryText"] = new { type = "string", description = "Compact tool-specific summary text." },
            ["status"] = new { type = "string", description = "Tool-specific status string." },
            ["navigation"] = new { type = "object", description = "Recommended next-step envelope." },
            ["nextSteps"] = new { type = "array", items = new { type = "object" }, description = "Compatibility next-step list." },
            ["contextRefs"] = new { type = "array", items = new { type = "object" }, description = "Structured references for follow-up tool calls." },
            ["pendingEvents"] = new { type = "array", items = new { type = "object" }, description = "Piggybacked runtime events when present." },
            ["pendingEventCount"] = new { type = "integer", description = "Number of piggybacked pending events." },
            ["droppedEventCount"] = new { type = "integer", description = "Number of dropped pending events when bounded buffers overflow." },
            ["cleanupIncomplete"] = new { type = "boolean", description = "Whether cleanup after a tool operation was incomplete." },
            ["cleanupFailureMessage"] = new { type = "string", description = "Cleanup failure details when cleanupIncomplete is true." },
            ["recovery"] = new { type = "object", description = "Machine-readable recovery guidance for structured errors." },
            ["errorData"] = new { type = "object", description = "Structured error context when available." },
            ["processes"] = new { type = "array", items = new { type = "object" }, description = "Inspectable or candidate WPF processes." },
            ["results"] = new { type = "array", items = new { type = "object" }, description = "Tool-specific result collection." },
            ["nodes"] = new { type = "array", items = new { type = "object" }, description = "Tree or scene node collection." },
            ["tree"] = new { type = "object", description = "Tree payload when a tool returns a rooted tree." },
            ["bindings"] = new { type = "array", items = new { type = "object" }, description = "Binding diagnostics or binding entries." },
            ["elements"] = new { type = "array", items = new { type = "object" }, description = "Element search or diagnostic entries." },
            ["snapshotId"] = new { type = "string", description = "Captured mutation-safety snapshot identifier." },
            ["diff"] = new { type = "object", description = "State diff payload." },
            ["screenshotId"] = new { type = "string", description = "Registered screenshot identifier." },
            ["resourceUri"] = new { type = "string", description = "MCP resource URI for retrievable payloads." },
            ["outputMode"] = new { type = "string", description = "Selected output mode for variant payloads." }
        }
    });

    public static void Apply(IEnumerable<Tool> tools)
    {
        foreach (var tool in tools)
        {
            Apply(tool);
        }
    }

    public static void Apply(Tool tool)
    {
        tool.OutputSchema = StructuredPayloadSchema.Clone();
    }
}
