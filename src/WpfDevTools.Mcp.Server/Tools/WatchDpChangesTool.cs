using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to watch DependencyProperty changes
/// </summary>
public sealed class WatchDpChangesTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the WatchDpChangesTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public WatchDpChangesTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the watch_dp_changes tool to monitor DependencyProperty value changes
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, elementId, and propertyName</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result indicating success or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var propertyName = ParseStringParam(arguments, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            return CreateMissingParamError("propertyName");

        return await SendInspectorRequestAsync(processId, "watch_dp_changes",
            new { elementId, propertyName }, cancellationToken);
    }
}
