using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class BatchMutateTool
{
    private static object CreateStepFailure(string stepName, JsonElement response) =>
        ToolRecoveryPayload.CreateStepFailure(
            $"Failed during {stepName}.",
            $"Inspect the failing {stepName} step before retrying batch_mutate.",
            response);
}
