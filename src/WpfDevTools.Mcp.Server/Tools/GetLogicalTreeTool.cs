using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get Logical Tree from WPF process
/// </summary>
public sealed class GetLogicalTreeTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetLogicalTreeTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetLogicalTreeTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_logical_tree tool to retrieve Logical Tree structure
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, optional elementId and depth</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing Logical Tree data or error</returns>
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

        return await SendInspectorRequestAsync(processId, "get_logical_tree",
            new { elementId, depth }, cancellationToken);
    }
}
