using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get event handlers from WPF elements
/// </summary>
public class GetEventHandlersTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetEventHandlersTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetEventHandlersTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_event_handlers tool to retrieve event handlers for a specific event
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, elementId, and eventName</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing event handler information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var eventName = ParseStringParam(arguments, "eventName");

        if (string.IsNullOrEmpty(eventName))
            return CreateMissingParamError("eventName");

        return await SendInspectorRequestAsync(processId, "get_event_handlers",
            new { elementId, eventName }, cancellationToken);
    }
}
