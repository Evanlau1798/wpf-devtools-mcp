using System.Text.Json;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get bindings from WPF elements
/// </summary>
public sealed class GetBindingsTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetBindingsTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetBindingsTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_bindings tool to retrieve all bindings on an element
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing binding information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var recursive = ParameterParser.ParseBoolParam(arguments, "recursive");

        return await SendInspectorRequestAsync(processId, "get_bindings",
            new { elementId, recursive }, cancellationToken);
    }
}
