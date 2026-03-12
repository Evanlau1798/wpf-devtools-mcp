using System.Text.Json;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class GetInteractionReadinessTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
{
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        if (string.IsNullOrWhiteSpace(elementId))
        {
            return CreateMissingParamError("elementId");
        }

        var interactionType = ParameterParser.ParseStringParam(arguments, "interactionType") ?? "Click";
        return await SendInspectorRequestAsync(
            processId,
            "get_interaction_readiness",
            new { elementId, interactionType },
            cancellationToken).ConfigureAwait(false);
    }
}
