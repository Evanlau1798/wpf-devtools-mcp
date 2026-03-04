using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get template tree from WPF elements
/// </summary>
public class GetTemplateTreeTool : PipeConnectedToolBase
{
    public GetTemplateTreeTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var maxDepth = ParseIntParam(arguments, "maxDepth");

        return await SendInspectorRequestAsync(processId, "get_template_tree",
            new { elementId, maxDepth }, cancellationToken);
    }
}
