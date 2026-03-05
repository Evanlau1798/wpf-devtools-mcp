using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get Visual Tree from WPF process
/// </summary>
public class GetVisualTreeTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetVisualTreeTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetVisualTreeTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_visual_tree tool to retrieve Visual Tree structure
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, optional elementId and depth</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing Visual Tree data or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var depth = ParseIntParam(arguments, "depth");

        // Validate depth parameter to prevent excessive recursion
        if (depth.HasValue && depth.Value > 100)
        {
            return new
            {
                success = false,
                error = "Depth parameter must be <= 100 to prevent excessive recursion"
            };
        }

        return await SendInspectorRequestAsync(processId, "get_visual_tree",
            new { elementId, depth }, cancellationToken);
    }
}
