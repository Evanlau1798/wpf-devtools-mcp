using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to fire routed events on WPF elements
/// </summary>
public sealed class FireRoutedEventTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the FireRoutedEventTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public FireRoutedEventTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the fire_routed_event tool to trigger a routed event on an element
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, elementId, and eventName</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result indicating success or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var eventName = ParseStringParam(arguments, "eventName");
        var eventArgs = WpfDevTools.Shared.Utilities.ParameterParser.ParseJsonParam(arguments, "eventArgs");

        if (string.IsNullOrEmpty(eventName))
            return CreateMissingParamError("eventName");

        return await SendInspectorRequestAsync(processId, "fire_routed_event",
            new { elementId, eventName, eventArgs }, cancellationToken);
    }
}
