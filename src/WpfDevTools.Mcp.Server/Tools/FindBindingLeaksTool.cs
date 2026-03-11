using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to find binding leaks in WPF application
/// </summary>
public sealed class FindBindingLeaksTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the FindBindingLeaksTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public FindBindingLeaksTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the find_binding_leaks tool to detect potential binding memory leaks
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional threshold</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing binding leak information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;
        var threshold = ParseIntParam(arguments, "threshold");

        return await SendInspectorRequestAsync(processId, "find_binding_leaks",
            new { threshold = threshold ?? 100 }, cancellationToken);
    }
}
