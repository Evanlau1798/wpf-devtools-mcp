using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get Visual Tree from WPF process
/// </summary>
public sealed class GetVisualTreeTool : PipeConnectedToolBase
{
    public GetVisualTreeTool(SessionManager sessionManager) : base(sessionManager) { }

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
            "get_visual_tree",
            options.ToInspectorParams(elementId),
            cancellationToken);
    }
}
