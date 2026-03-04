using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get validation errors from WPF application
/// </summary>
public class GetValidationErrorsTool : PipeConnectedToolBase
{
    public GetValidationErrorsTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_validation_errors",
            new { elementId }, cancellationToken);
    }
}
