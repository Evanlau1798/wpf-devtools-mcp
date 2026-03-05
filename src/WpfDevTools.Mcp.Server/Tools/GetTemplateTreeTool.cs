using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get template tree from WPF elements
/// </summary>
public class GetTemplateTreeTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetTemplateTreeTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetTemplateTreeTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_template_tree tool to retrieve control template structure
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, optional elementId, and optional depth</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing template tree data or error</returns>
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
