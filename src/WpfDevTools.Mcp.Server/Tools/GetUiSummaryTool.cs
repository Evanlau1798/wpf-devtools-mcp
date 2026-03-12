using System.Text.Json;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Executes the get_ui_summary scene diagnostic tool.
/// </summary>
public sealed class GetUiSummaryTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
{
    /// <summary>
    /// Execute the get_ui_summary request.
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        var elementId = ParameterParser.ParseStringParam(arguments, "elementId");
        var depth = ParameterParser.ParseIntParam(arguments, "depth");
        var depthMode = ParameterParser.ParseStringParam(arguments, "depthMode");

        return await SendInspectorRequestAsync(
            processId,
            "get_ui_summary",
            new { elementId, depth, depthMode },
            cancellationToken).ConfigureAwait(false);
    }
}
