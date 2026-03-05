using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get ViewModel from WPF elements
/// </summary>
public class GetViewModelTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetViewModelTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetViewModelTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_viewmodel tool to retrieve ViewModel (DataContext) information
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing ViewModel information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_viewmodel",
            new { elementId }, cancellationToken);
    }
}
