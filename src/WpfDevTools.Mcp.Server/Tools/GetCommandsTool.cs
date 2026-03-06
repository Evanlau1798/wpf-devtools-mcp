using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get commands from WPF elements
/// </summary>
public sealed class GetCommandsTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetCommandsTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetCommandsTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_commands tool to retrieve available commands on an element
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing command information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "get_commands",
            new { elementId }, cancellationToken);
    }
}
