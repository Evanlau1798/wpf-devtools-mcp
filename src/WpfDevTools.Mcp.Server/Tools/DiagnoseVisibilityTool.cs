using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class DiagnoseVisibilityTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
{
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        if (string.IsNullOrWhiteSpace(elementId))
        {
            return CreateMissingParamError("elementId");
        }

        return await SendInspectorRequestAsync(
            processId,
            "diagnose_visibility",
            new { elementId },
            cancellationToken).ConfigureAwait(false);
    }
}
