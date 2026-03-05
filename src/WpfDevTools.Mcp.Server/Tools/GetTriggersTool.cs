using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get triggers from WPF element styles
/// </summary>
public class GetTriggersTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetTriggersTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetTriggersTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_triggers tool to retrieve style and template triggers
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing trigger information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_triggers",
            new { elementId }, cancellationToken);
    }
}
