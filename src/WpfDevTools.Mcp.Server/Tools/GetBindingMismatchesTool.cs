using System.Text.Json;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class GetBindingMismatchesTool : PipeConnectedToolBase
{
    public GetBindingMismatchesTool(SessionManager sessionManager) : base(sessionManager)
    {
    }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        var recursive = ParameterParser.ParseBoolParam(arguments, "recursive");
        var includeFramework = ParameterParser.ParseBoolParam(arguments, "includeFramework");
        return await SendInspectorRequestAsync(
            processId,
            "get_binding_mismatches",
            new { elementId, recursive, includeFramework },
            cancellationToken).ConfigureAwait(false);
    }
}
