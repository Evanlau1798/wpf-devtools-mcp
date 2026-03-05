using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to trace routed events in WPF application
/// </summary>
public class TraceRoutedEventsTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the TraceRoutedEventsTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public TraceRoutedEventsTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the trace_routed_events tool to enable event tracing
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, elementId, and eventName</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result indicating success or error</returns>
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
