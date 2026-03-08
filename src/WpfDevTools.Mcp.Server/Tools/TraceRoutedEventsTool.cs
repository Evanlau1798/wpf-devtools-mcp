using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to trace routed events in WPF application
/// </summary>
public sealed class TraceRoutedEventsTool : PipeConnectedToolBase
{
    public TraceRoutedEventsTool(SessionManager sessionManager) : base(sessionManager) { }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
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

        return await SendInspectorRequestAsync(
            processId,
            "trace_routed_events",
            new { elementId, eventName, duration, mode },
            cancellationToken);
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
}
