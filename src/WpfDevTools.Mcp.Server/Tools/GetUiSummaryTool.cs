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
        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
            arguments,
            "depth",
            0,
            TreeRequestOptions.MaxDepthLimit,
            out var depth,
            out var depthError))
        {
            return depthError!;
        }

        if (!BoundaryParameterValidator.TryGetOptionalStringEnum(
            arguments,
            "depthMode",
            "semantic",
            ["semantic", "visual"],
            out var depthMode,
            out var depthModeError))
        {
            return depthModeError!;
        }

        var summaryOnly = ParameterParser.ParseBoolParam(arguments, "summaryOnly");

        return await SendInspectorRequestAsync(
            processId,
            "get_ui_summary",
            new { elementId, depth, depthMode, summaryOnly = summaryOnly ?? false },
            cancellationToken).ConfigureAwait(false);
    }
}
