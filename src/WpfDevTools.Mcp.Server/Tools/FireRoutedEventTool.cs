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
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;
        var (detailMode, detailError) = ParseMutationDetailMode(arguments);
        if (detailError != null) return detailError;
        var eventName = ParseStringParam(arguments, "eventName");
        var eventArgs = WpfDevTools.Shared.Utilities.ParameterParser.ParseJsonParam(arguments, "eventArgs");

        if (elementId == null)
            return CreateMissingParamError("elementId");

        if (string.IsNullOrEmpty(eventName))
            return CreateMissingParamError("eventName");

        var requestedInput = new { elementId, eventName, eventArgs };
        var result = await SendInspectorRequestAsync(
            processId,
            "fire_routed_event",
            requestedInput,
            cancellationToken);

        var resultJson = JsonSerializer.SerializeToElement(result);
        var usedFallback = resultJson.ValueKind == JsonValueKind.Object &&
            resultJson.TryGetProperty("usedOnClick", out var usedOnClickProp) &&
            usedOnClickProp.ValueKind == JsonValueKind.True;

        return AddSuccessMetadata(
            result,
            requestedInput,
            "Routed-event execution may use the ButtonBase OnClick path when applicable. Inspect usedFallback and observedEffect before assuming the event path used.",
            usedFallback,
            detailMode);
    }
}
