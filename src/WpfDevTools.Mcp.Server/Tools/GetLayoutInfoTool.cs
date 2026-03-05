using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get layout information from WPF elements
/// </summary>
public class GetLayoutInfoTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetLayoutInfoTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetLayoutInfoTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_layout_info tool to retrieve layout information for an element
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing layout information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_layout_info",
            new { elementId }, cancellationToken);
    }
}
