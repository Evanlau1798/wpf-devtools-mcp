using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to find binding leaks in WPF application
/// </summary>
public class FindBindingLeaksTool : PipeConnectedToolBase
{
    public FindBindingLeaksTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var threshold = ParseIntParam(arguments, "threshold");

        return await SendInspectorRequestAsync(processId, "find_binding_leaks",
            new { threshold = threshold ?? 100 }, cancellationToken);
    }
}
