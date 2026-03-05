using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to invalidate layout for WPF elements
/// </summary>
public class InvalidateLayoutTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the InvalidateLayoutTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public InvalidateLayoutTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the invalidate_layout tool to force layout recalculation
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result indicating success or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "invalidate_layout",
            new { elementId }, cancellationToken);
    }
}
