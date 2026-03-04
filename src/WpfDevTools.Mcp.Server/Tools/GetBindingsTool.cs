using System.Text.Json;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get bindings from WPF elements
/// </summary>
public class GetBindingsTool : PipeConnectedToolBase
{
    public GetBindingsTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var recursive = ParameterParser.ParseBoolParam(arguments, "recursive");

        return await SendInspectorRequestAsync(processId, "get_bindings",
            new { elementId, recursive }, cancellationToken);
    }
}
