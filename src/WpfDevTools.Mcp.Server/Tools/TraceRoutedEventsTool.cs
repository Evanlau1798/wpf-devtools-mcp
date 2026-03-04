using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to trace routed events in WPF application
/// </summary>
public class TraceRoutedEventsTool : PipeConnectedToolBase
{
    public TraceRoutedEventsTool(SessionManager sessionManager) : base(sessionManager) { }

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

        return await SendInspectorRequestAsync(processId, "trace_routed_events",
            new { elementId, eventName }, cancellationToken);
    }
}
