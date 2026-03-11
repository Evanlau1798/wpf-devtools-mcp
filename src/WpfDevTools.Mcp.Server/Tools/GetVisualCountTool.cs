using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get visual element count from WPF application
/// </summary>
public sealed class GetVisualCountTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetVisualCountTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetVisualCountTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_visual_count tool to count visual elements in the tree
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing visual element count or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_visual_count",
            new { elementId }, cancellationToken);
    }
}
