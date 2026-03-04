using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get event handlers from WPF elements
/// </summary>
public class GetEventHandlersTool : PipeConnectedToolBase
{
    public GetEventHandlersTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_event_handlers",
            new { elementId }, cancellationToken);
    }
}
