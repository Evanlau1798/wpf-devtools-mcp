using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to fire routed events on WPF elements
/// </summary>
public class FireRoutedEventTool : PipeConnectedToolBase
{
    public FireRoutedEventTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var eventName = ParseStringParam(arguments, "eventName");

        if (string.IsNullOrEmpty(eventName))
            return CreateMissingParamError("eventName");

        return await SendInspectorRequestAsync(processId, "fire_routed_event",
            new { elementId, eventName }, cancellationToken);
    }
}
