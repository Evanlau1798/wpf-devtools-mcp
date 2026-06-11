using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to scroll WPF elements into view
/// </summary>
public sealed class ScrollToElementTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the ScrollToElementTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public ScrollToElementTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the scroll_to_element tool to bring an element into view
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result indicating success or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "scroll_to_element",
            new { elementId }, cancellationToken);
    }
}
