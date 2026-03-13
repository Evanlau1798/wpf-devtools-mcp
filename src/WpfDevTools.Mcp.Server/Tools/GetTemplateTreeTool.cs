using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get template tree from WPF elements
/// </summary>
public sealed class GetTemplateTreeTool : PipeConnectedToolBase
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
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;
        var depth = ParseIntParam(arguments, "depth");

        if (depth.HasValue && depth.Value > 100)
            return new ToolErrorPayload
            {
                Error = "depth must be between 0 and 100. Use smaller depth values for better performance (depth=2-3 recommended for most cases).",
                ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
                Hint = "Provide a depth between 0 and 100. For most template inspections, start with depth=2 or depth=3."
            };

        return await SendInspectorRequestAsync(processId, "get_template_tree",
            new { elementId, depth }, cancellationToken);
    }
}
