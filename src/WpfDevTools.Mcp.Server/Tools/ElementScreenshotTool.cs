using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to capture screenshots of WPF elements
/// </summary>
public class ElementScreenshotTool : PipeConnectedToolBase
{
    public ElementScreenshotTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var outputPath = ParseStringParam(arguments, "outputPath");

        return await SendInspectorRequestAsync(processId, "element_screenshot",
            new { elementId, outputPath }, cancellationToken);
    }
}
