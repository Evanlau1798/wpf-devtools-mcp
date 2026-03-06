using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get render statistics from WPF application
/// </summary>
public sealed class GetRenderStatsTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetRenderStatsTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetRenderStatsTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_render_stats tool to retrieve rendering performance statistics
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing render statistics or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_render_stats",
            new { }, cancellationToken);
    }
}
