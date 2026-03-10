using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to execute commands in WPF application
/// </summary>
public sealed class ExecuteCommandTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the ExecuteCommandTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public ExecuteCommandTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the execute_command tool to invoke a command in the WPF application
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, elementId, commandName, and optional parameter</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result indicating success or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var commandName = ParseStringParam(arguments, "commandName");
        var parameter = ParseStringParam(arguments, "parameter");

        if (string.IsNullOrEmpty(commandName))
            return CreateMissingParamError("commandName");

        var requestedInput = new { elementId, commandName, parameter };
        var result = await SendInspectorRequestAsync(
            processId,
            "execute_command",
            requestedInput,
            cancellationToken);

        return AddSuccessMetadata(
            result,
            requestedInput,
            "Triggers real application logic. Confirm the observedEffect before assuming navigation, save, or side effects completed.");
    }
}
