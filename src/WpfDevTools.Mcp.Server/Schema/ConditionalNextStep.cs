using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Schema;

public static class ConditionalNextStep
{
    public static ToolNextStep Create(
        string tool,
        JsonElement @params,
        string reason,
        ToolNextStepKind kind,
        int priority,
        IReadOnlyList<string>? preconditions = null,
        string? expectedOutcome = null,
        string? workflowId = null,
        IReadOnlyList<string>? prefetchTools = null) =>
        new(
            tool,
            @params,
            reason,
            kind,
            priority,
            preconditions,
            expectedOutcome,
            workflowId,
            prefetchTools);
}
