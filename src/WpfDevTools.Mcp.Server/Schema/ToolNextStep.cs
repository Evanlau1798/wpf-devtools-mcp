using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Schema;

public sealed record ToolNextStep(
    string Tool,
    JsonElement Params,
    string Reason,
    ToolNextStepKind Kind,
    int Priority);
