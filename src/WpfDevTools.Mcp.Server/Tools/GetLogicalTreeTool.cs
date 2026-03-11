using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get Logical Tree from WPF process
/// </summary>
public sealed class GetLogicalTreeTool : PipeConnectedToolBase
{
    public GetLogicalTreeTool(SessionManager sessionManager) : base(sessionManager) { }

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;

        if (!TreeRequestOptions.TryParse(arguments, out var options, out var optionsError))
        {
            return optionsError!;
        }

        return await SendInspectorRequestAsync(
            processId,
            "get_logical_tree",
            options.ToInspectorParams(elementId),
            cancellationToken);
    }
}
