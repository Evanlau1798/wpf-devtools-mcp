using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get binding errors from WPF application
/// </summary>
public sealed class GetBindingErrorsTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetBindingErrorsTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetBindingErrorsTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_binding_errors tool to retrieve all binding errors
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing binding error information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;
        var maxErrors = arguments.HasValue && arguments.Value.TryGetProperty("maxErrors", out var maxErrorsProperty)
            && maxErrorsProperty.ValueKind == JsonValueKind.Number
            ? maxErrorsProperty.GetInt32()
            : (int?)null;
        var sinceTimestamp = arguments.HasValue && arguments.Value.TryGetProperty("sinceTimestamp", out var sinceProperty)
            && sinceProperty.ValueKind == JsonValueKind.String
            ? sinceProperty.GetString()
            : null;
        var compact = arguments.HasValue && arguments.Value.TryGetProperty("compact", out var compactProperty)
            && compactProperty.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? compactProperty.GetBoolean()
            : (bool?)null;

        return await SendInspectorRequestWithPiggybackAsync(processId, "get_binding_errors",
            // Keep full binding messages in the pipe payload so navigation can classify
            // trace-only errors before compact trimming is applied to the client response.
            new { maxErrors, sinceTimestamp, compact = false }, cancellationToken);
    }
}
