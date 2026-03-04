using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get Logical Tree from WPF process
/// </summary>
public class GetLogicalTreeTool : PipeConnectedToolBase
{
    public GetLogicalTreeTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var depth = ParseIntParam(arguments, "depth");

        return await SendInspectorRequestAsync(processId, "get_logical_tree",
            new { elementId, depth }, cancellationToken);
    }
}
