using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to simulate keyboard input on WPF elements
/// </summary>
public class SimulateKeyboardTool : PipeConnectedToolBase
{
    public SimulateKeyboardTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var key = ParseStringParam(arguments, "key");

        if (string.IsNullOrEmpty(key))
            return CreateMissingParamError("key");

        return await SendInspectorRequestAsync(processId, "simulate_keyboard",
            new { elementId, key }, cancellationToken);
    }
}
