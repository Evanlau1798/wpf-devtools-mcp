using System.Text.Json;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Executes the get_form_summary scene diagnostic tool.
/// </summary>
public sealed class GetFormSummaryTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
{
    /// <summary>
    /// Execute the get_form_summary request.
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        var elementId = ParameterParser.ParseStringParam(arguments, "elementId");
        var includeFramework = ParameterParser.ParseBoolParam(arguments, "includeFramework");

        return await SendInspectorRequestAsync(
            processId,
            "get_form_summary",
            new { elementId, includeFramework },
            cancellationToken).ConfigureAwait(false);
    }
}
