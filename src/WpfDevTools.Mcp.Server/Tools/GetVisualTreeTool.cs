using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get Visual Tree from WPF process
/// </summary>
public class GetVisualTreeTool : PipeConnectedToolBase
{
    public GetVisualTreeTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var depth = ParseIntParam(arguments, "depth");

        return await SendInspectorRequestAsync(processId, "get_visual_tree",
            new { elementId, depth }, cancellationToken);
    }
}
