using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to capture screenshots of WPF elements
/// </summary>
public sealed class ElementScreenshotTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the ElementScreenshotTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public ElementScreenshotTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the element_screenshot tool to capture a screenshot of an element
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing screenshot path or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "element_screenshot",
            new { elementId }, cancellationToken);
    }
}
