using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get clipping information from WPF elements
/// </summary>
public sealed class GetClippingInfoTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetClippingInfoTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetClippingInfoTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_clipping_info tool to retrieve element clipping information
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing clipping information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_clipping_info",
            new { elementId }, cancellationToken);
    }
}
