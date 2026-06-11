using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Schema;

public sealed record ToolNextStep(
    string Tool,
    JsonElement Params,
    string Reason,
    ToolNextStepKind Kind,
    int Priority,
    IReadOnlyList<string>? Preconditions = null,
    string? ExpectedOutcome = null,
    string? WorkflowId = null,
    IReadOnlyList<string>? PrefetchTools = null,
    string? WhyNow = null,
    string? Confidence = null);
