using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get resource chain from WPF elements
/// </summary>
public class GetResourceChainTool : PipeConnectedToolBase
{
    public GetResourceChainTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var resourceKey = ParseStringParam(arguments, "resourceKey");

        return await SendInspectorRequestAsync(processId, "get_resource_chain",
            new { elementId, resourceKey }, cancellationToken);
    }
}
