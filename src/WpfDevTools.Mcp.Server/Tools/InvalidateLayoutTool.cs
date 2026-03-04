using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to invalidate layout for WPF elements
/// </summary>
public class InvalidateLayoutTool : PipeConnectedToolBase
{
    public InvalidateLayoutTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "invalidate_layout",
            new { elementId }, cancellationToken);
    }
}
