using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get DependencyProperty value source
/// </summary>
public class GetDpValueSourceTool : PipeConnectedToolBase
{
    public GetDpValueSourceTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
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
