using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get applied styles from WPF elements
/// </summary>
public class GetAppliedStylesTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetAppliedStylesTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetAppliedStylesTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_applied_styles tool to retrieve applied styles on an element
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing applied styles information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_applied_styles",
            new { elementId }, cancellationToken);
    }
}
