using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get DataContext chain from WPF elements
/// </summary>
public sealed class GetDataContextChainTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetDataContextChainTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetDataContextChainTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_datacontext_chain tool to retrieve DataContext inheritance chain
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing DataContext chain information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_datacontext_chain",
            new { elementId }, cancellationToken);
    }
}
