using System.Text.Json;
using WpfDevTools.Mcp.Server.Navigation;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to trace routed events in WPF application
/// </summary>
public sealed class TraceRoutedEventsTool : PipeConnectedToolBase
{
    public TraceRoutedEventsTool(SessionManager sessionManager) : base(sessionManager) { }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        var mode = NormalizeMode(ParseStringParam(arguments, "mode"));
        if (mode == null)
        {
            return CreateInvalidParamError("mode must be one of: capture, start, get");
        }

        var eventName = ParseStringParam(arguments, "eventName");
        var duration = ParseIntParam(arguments, "duration");

        if (mode != "get" && string.IsNullOrEmpty(eventName))
        {
            return CreateMissingParamError("eventName");
        }

        var response = await SendInspectorRequestAsync(
            processId,
            "trace_routed_events",
            new { elementId, eventName, duration, mode },
            cancellationToken);

        SynchronizeTraceNavigationState(processId, elementId, eventName, mode, response);
        return response;
    }

    private static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "capture";
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            "capture" => "capture",
            "start" => "start",
            "get" => "get",
            _ => null
        };
    }

    private void SynchronizeTraceNavigationState(
        int processId,
        string? elementId,
        string? eventName,
        string mode,
        object response)
    {
        var payload = JsonSerializer.SerializeToElement(response);
        if (!IsSuccess(payload))
        {
            return;
        }

        if (mode == "start" && IsTracing(payload))
        {
            _sessionManager.SetActiveTraceState(
                processId,
                new ActiveTraceNavigationState(
                    eventName ?? GetOptionalString(payload, "eventName") ?? string.Empty,
                    elementId,
                    DateTimeOffset.UtcNow,
                    GetEffectiveDuration(payload)));
            return;
        }

        if (mode == "get" && !IsTracing(payload))
        {
            _sessionManager.ClearActiveTraceState(processId);
        }
    }

    private static bool IsSuccess(JsonElement payload) =>
        payload.TryGetProperty("success", out var success) && success.GetBoolean();

    private static bool IsTracing(JsonElement payload) =>
        payload.TryGetProperty("isTracing", out var isTracing) && isTracing.GetBoolean();

    private static TimeSpan GetEffectiveDuration(JsonElement payload) =>
        payload.TryGetProperty("effectiveDuration", out var durationProperty)
        && durationProperty.ValueKind == JsonValueKind.Number
        && durationProperty.TryGetInt32(out var milliseconds)
        && milliseconds > 0
            ? TimeSpan.FromMilliseconds(milliseconds)
            : TimeSpan.Zero;

    private static string? GetOptionalString(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
