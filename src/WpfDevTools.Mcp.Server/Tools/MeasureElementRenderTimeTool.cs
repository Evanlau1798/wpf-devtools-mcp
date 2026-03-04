using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to measure element render time in WPF application
/// </summary>
public class MeasureElementRenderTimeTool : PipeConnectedToolBase
{
    public MeasureElementRenderTimeTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "measure_element_render_time",
            new { elementId }, cancellationToken);
    }
}
