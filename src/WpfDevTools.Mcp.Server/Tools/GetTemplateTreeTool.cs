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
        var depth = ParseIntParam(arguments, "depth");

        if (depth.HasValue && depth.Value > 100)
            return new { success = false, error = "depth must be between 0 and 100" };

        return await SendInspectorRequestAsync(processId, "get_template_tree",
            new { elementId, depth }, cancellationToken);
    }
}
