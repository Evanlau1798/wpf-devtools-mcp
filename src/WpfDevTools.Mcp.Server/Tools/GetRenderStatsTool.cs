using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get render statistics from WPF application
/// </summary>
public class GetRenderStatsTool : PipeConnectedToolBase
{
    public GetRenderStatsTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_render_stats",
            new { }, cancellationToken);
    }
}
