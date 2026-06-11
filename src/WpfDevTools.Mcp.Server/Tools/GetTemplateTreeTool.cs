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

        if (!TreeRequestOptions.TryParse(arguments, out var options, out var optionsError))
        {
            return optionsError!;
        }

        return await SendInspectorRequestAsync(processId, "get_template_tree",
            new
            {
                elementId,
                depth = options.Depth,
                compact = options.Compact,
                maxNodes = options.MaxNodes,
                maxChildrenPerNode = options.MaxChildrenPerNode
            }, cancellationToken);
    }
}
