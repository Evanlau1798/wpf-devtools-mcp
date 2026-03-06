using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get DependencyProperty value source
/// </summary>
public sealed class GetDpValueSourceTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetDpValueSourceTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetDpValueSourceTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_dp_value_source tool to retrieve DependencyProperty value source
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, elementId, and propertyName</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing value source information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var propertyName = ParseStringParam(arguments, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            return CreateMissingParamError("propertyName");

        return await SendInspectorRequestAsync(processId, "get_dp_value_source",
            new { elementId, propertyName }, cancellationToken);
    }
}
