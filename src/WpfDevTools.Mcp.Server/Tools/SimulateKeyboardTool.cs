using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to simulate keyboard input on WPF elements
/// </summary>
public sealed class SimulateKeyboardTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the SimulateKeyboardTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public SimulateKeyboardTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the simulate_keyboard tool to send keyboard input to an element
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, elementId, and key</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result indicating success or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;
        var key = ParseStringParam(arguments, "key");
        var eventType = ParseStringParam(arguments, "eventType");

        if (string.IsNullOrEmpty(key))
            return CreateMissingParamError("key");

        return await SendInspectorRequestAsync(processId, "simulate_keyboard",
            new { elementId, key, eventType }, cancellationToken);
    }
}
