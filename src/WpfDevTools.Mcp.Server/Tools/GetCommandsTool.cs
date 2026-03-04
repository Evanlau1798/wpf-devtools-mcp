using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get commands from WPF elements
/// </summary>
public class GetCommandsTool : PipeConnectedToolBase
{
    public GetCommandsTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_commands",
            new { elementId }, cancellationToken);
    }
}
