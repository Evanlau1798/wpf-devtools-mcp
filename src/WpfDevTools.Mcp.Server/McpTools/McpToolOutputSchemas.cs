using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace WpfDevTools.Mcp.Server.McpTools;

internal static class McpToolOutputSchemas
{
    private static readonly JsonElement StructuredPayloadSchema = CreateSchema(
        additionalProperties: true,
        toolSpecificProperties: []);

    private static readonly Dictionary<string, JsonElement> HighValueSchemas = CreateHighValueSchemas();

    public static void Apply(IEnumerable<Tool> tools)
    {
        foreach (var tool in tools)
        {
            Apply(tool);
        }
    }

    public static void Apply(Tool tool)
    {
        tool.OutputSchema = HighValueSchemas.TryGetValue(tool.Name, out var schema)
            ? schema.Clone()
            : StructuredPayloadSchema.Clone();
    }

    internal static string GetSchemaStatus(string toolName)
        => HighValueSchemas.ContainsKey(toolName)
            ? "exact-tool-output-schema"
            : "generic-structured-payload";

    internal static string GetSchemaHashSource(string toolName)
    {
        var status = GetSchemaStatus(toolName);
        var schema = HighValueSchemas.TryGetValue(toolName, out var highValueSchema)
            ? highValueSchema
            : StructuredPayloadSchema;

        return status + ":" + schema.GetRawText();
    }

    private static Dictionary<string, JsonElement> CreateHighValueSchemas()
        => new(StringComparer.Ordinal)
        {
            ["connect"] = CreateSchema(false, new()
            {
                ["processId"] = Integer("Selected or target WPF process identifier."),
                ["processName"] = String("Selected process name."),
                ["windowTitle"] = String("Selected WPF window title."),
                ["autoDiscovered"] = Boolean("Whether connect selected an allowlisted target automatically."),
                ["autoSelected"] = Boolean("Whether a selection strategy selected the target."),
                ["selectionReason"] = String("Reason for the automatic selection."),
                ["candidateCount"] = Integer("Number of allowlisted candidate targets."),
                ["redactedCandidateCount"] = Integer("Number of denied candidates hidden by policy."),
                ["policyEnvVar"] = String("Policy environment variable relevant to the result."),
                ["processes"] = ArrayOfObject("Allowed candidate processes when disambiguation is required."),
                ["requiresElevationToConnect"] = Boolean("Whether the server must run elevated to connect."),
                ["canConnectFromCurrentServer"] = Boolean("Whether the current server can connect directly."),
                ["suggestedAction"] = String("Operator-oriented next step for failed connection attempts.")
            }),
            ["get_processes"] = CreateSchema(false, new()
            {
                ["processes"] = ArrayOfObject("Allowlisted WPF processes visible to this server."),
                ["redactedTargetCount"] = Integer("Number of denied targets hidden by policy before filtering."),
                ["policyEnvVar"] = String("Policy environment variable controlling process visibility.")
            }),
            ["get_ui_summary"] = CreateSchema(false, new()
            {
                ["rootElementId"] = String("Runtime element id for the summarized root."),
                ["rootElementType"] = String("WPF type for the summarized root."),
                ["rootElementName"] = String("Name for the summarized root when available."),
                ["depth"] = Integer("Traversal depth used to build the summary."),
                ["semanticNodeCount"] = Integer("Number of semantic nodes returned."),
                ["summaryText"] = String("Compact scene summary for AI clients."),
                ["nodes"] = ArrayOfObject("Semantic scene nodes.")
            }),
            ["get_element_snapshot"] = CreateSchema(false, new()
            {
                ["elementId"] = String("Runtime element id."),
                ["elementType"] = String("WPF element type."),
                ["elementName"] = String("Element name when available."),
                ["dataContextType"] = String("DataContext type name when available."),
                ["properties"] = Object("DependencyProperty snapshot."),
                ["bindings"] = ArrayOfObject("Binding entries for the element."),
                ["validationErrors"] = ArrayOfObject("Validation errors for the element."),
                ["style"] = Object("Style and template summary."),
                ["layout"] = Object("Layout summary.")
            }),
            ["get_bindings"] = CreateSchema(false, new()
            {
                ["bindings"] = ArrayOfObject("Binding entries for single-target inspection."),
                ["results"] = ArrayOfObject("Per-target batch binding results."),
                ["resultCount"] = Integer("Number of batch results."),
                ["successCount"] = Integer("Number of successful batch results."),
                ["failureCount"] = Integer("Number of failed batch results.")
            }),
            ["get_binding_errors"] = CreateSchema(false, new()
            {
                ["errorCount"] = Integer("Number of binding errors."),
                ["errors"] = ArrayOfObject("Binding error records."),
                ["navigation"] = Navigation(),
                ["nextSteps"] = ArrayOfObject("Compatibility next-step list.")
            }),
            ["capture_state_snapshot"] = CreateSchema(false, new()
            {
                ["snapshotId"] = String("Captured mutation-safety snapshot id."),
                ["snapshotSummary"] = Object("Summary of captured DependencyProperty, ViewModel, and focus state.")
            }),
            ["get_state_diff"] = CreateSchema(false, new()
            {
                ["snapshotId"] = String("Snapshot id used for the diff."),
                ["trigger"] = String("Human-readable trigger label for the observed change."),
                ["diff"] = Object("State diff payload.")
            }),
            ["restore_state_snapshot"] = CreateSchema(false, new()
            {
                ["snapshotId"] = String("Snapshot id restored."),
                ["restoredDependencyProperties"] = ArrayOfObject("DependencyProperty entries restored."),
                ["restoredViewModelProperties"] = ArrayOfObject("ViewModel properties restored."),
                ["skippedDependencyProperties"] = ArrayOfObject("DependencyProperty entries skipped with reasons."),
                ["skippedViewModelProperties"] = ArrayOfObject("ViewModel properties skipped with reasons.")
            }),
            ["batch_mutate"] = CreateSchema(false, new()
            {
                ["results"] = ArrayOfObject("Per-mutation results in execution order."),
                ["mutationCount"] = Integer("Total mutation count."),
                ["successfulMutationCount"] = Integer("Number of successful mutations."),
                ["failedMutationCount"] = Integer("Number of failed mutations."),
                ["snapshotId"] = String("Snapshot captured for the batch when requested."),
                ["diff"] = Object("Optional diff computed after successful mutations."),
                ["rollback"] = Object("Rollback availability and restore parameters."),
                ["recovery"] = Object("Machine-readable recovery guidance.")
            }),
            ["element_screenshot"] = CreateSchema(false, new()
            {
                ["elementId"] = String("Runtime element id captured."),
                ["screenshotId"] = String("Registered screenshot identifier."),
                ["resourceUri"] = String("MCP resource URI for file-mode screenshot retrieval."),
                ["expiresAtUtc"] = String("UTC expiration timestamp for the retained screenshot resource."),
                ["outputMode"] = String("Selected screenshot output mode."),
                ["width"] = Integer("Screenshot width in pixels."),
                ["height"] = Integer("Screenshot height in pixels."),
                ["mimeType"] = String("Image MIME type."),
                ["base64Image"] = String("Inline base64 image data when explicitly requested and below the inline byte limit."),
                ["maxInlineByteLength"] = Integer("Inline base64 PNG byte limit reported on oversized inline requests.")
            })
        };

    private static JsonElement CreateSchema(
        bool additionalProperties,
        Dictionary<string, object> toolSpecificProperties)
    {
        var properties = CommonProperties();
        foreach (var property in toolSpecificProperties)
        {
            properties[property.Key] = property.Value;
        }

        return JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "success" },
            additionalProperties,
            properties
        });
    }

    private static Dictionary<string, object> CommonProperties()
        => new(StringComparer.Ordinal)
        {
            ["success"] = Boolean("Whether the tool operation succeeded."),
            ["error"] = String("Human-readable error message when success is false."),
            ["errorCode"] = String("Machine-readable error code when available."),
            ["message"] = String("Human-readable status or summary message."),
            ["status"] = String("Tool-specific status string."),
            ["summaryText"] = String("Compact tool-specific summary text."),
            ["navigation"] = Navigation(),
            ["nextSteps"] = ArrayOfObject("Compatibility next-step list."),
            ["pendingEvents"] = ArrayOfObject("Piggybacked runtime events when present."),
            ["pendingEventCount"] = Integer("Number of piggybacked pending events."),
            ["droppedEventCount"] = Integer("Number of dropped pending events when bounded buffers overflow."),
            ["cleanupIncomplete"] = Boolean("Whether cleanup after a tool operation was incomplete."),
            ["cleanupFailureMessage"] = String("Cleanup failure details when cleanupIncomplete is true."),
            ["recovery"] = Object("Machine-readable recovery guidance for structured errors."),
            ["errorData"] = Object("Structured error context when available.")
        };

    private static object Navigation()
        => new
        {
            type = "object",
            description = "Recommended next-step envelope.",
            additionalProperties = false,
            properties = new Dictionary<string, object>
            {
                ["recommended"] = ArrayOfObject("Primary next-step recommendations."),
                ["alternatives"] = ArrayOfObject("Alternative follow-up actions."),
                ["prefetchTools"] = ArrayOfString("Useful tools to inspect before choosing a follow-up."),
                ["contextRefs"] = ArrayOfObject("Structured references for follow-up tool calls.")
            }
        };

    private static object Object(string description)
        => new { type = "object", additionalProperties = true, description };

    private static object ArrayOfObject(string description)
        => new { type = "array", items = new { type = "object" }, description };

    private static object ArrayOfString(string description)
        => new { type = "array", items = new { type = "string" }, description };

    private static object String(string description)
        => new { type = "string", description };

    private static object Integer(string description)
        => new { type = "integer", description };

    private static object Boolean(string description)
        => new { type = "boolean", description };
}
