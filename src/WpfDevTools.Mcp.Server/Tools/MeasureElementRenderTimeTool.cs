using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to measure element render time in WPF application
/// </summary>
public class MeasureElementRenderTimeTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the MeasureElementRenderTimeTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public MeasureElementRenderTimeTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the measure_element_render_time tool to measure rendering performance
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing render time measurements or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, "measure_element_render_time",
            new { elementId }, cancellationToken);
    }
}
