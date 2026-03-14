using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class GetAffectedElementsTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
{
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        var propertyName = ParseStringParam(arguments, "propertyName");
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return CreateMissingParamError("propertyName");
        }

        var viewModelType = ParseStringParam(arguments, "viewModelType");
        var recursive = ParseBoolParam(arguments, "recursive") ?? true;

        return await SendInspectorRequestAsync(
            processId,
            "get_affected_elements",
            new { elementId, propertyName, viewModelType, recursive },
            cancellationToken).ConfigureAwait(false);
    }
}
