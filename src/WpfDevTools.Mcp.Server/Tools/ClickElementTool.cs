using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to simulate mouse click on WPF elements
/// </summary>
public class ClickElementTool : PipeConnectedToolBase
{
    public ClickElementTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "click_element",
            new { elementId }, cancellationToken);
    }
}
