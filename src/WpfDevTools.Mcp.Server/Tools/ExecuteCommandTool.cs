using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to execute commands in WPF application
/// </summary>
public class ExecuteCommandTool : PipeConnectedToolBase
{
    public ExecuteCommandTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var commandName = ParseStringParam(arguments, "commandName");
        var parameter = ParseStringParam(arguments, "parameter");

        if (string.IsNullOrEmpty(commandName))
            return CreateMissingParamError("commandName");

        return await SendInspectorRequestAsync(processId, "execute_command",
            new { elementId, commandName, parameter }, cancellationToken);
    }
}
