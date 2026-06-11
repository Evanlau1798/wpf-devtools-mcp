using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get resource chain from WPF elements
/// </summary>
public sealed class GetResourceChainTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetResourceChainTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetResourceChainTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_resource_chain tool to retrieve resource lookup chain for a key
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, optional elementId, and optional resourceKey</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing resource chain information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;
        var resourceKey = ParseStringParam(arguments, "resourceKey");

        return await SendInspectorRequestAsync(processId, "get_resource_chain",
            new { elementId, resourceKey }, cancellationToken);
    }
}
