using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to simulate mouse click on WPF elements
/// </summary>
public class ClickElementTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the ClickElementTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public ClickElementTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the click_element tool to simulate mouse click on an element
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result indicating success or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "click_element",
            new { elementId }, cancellationToken);
    }
}
