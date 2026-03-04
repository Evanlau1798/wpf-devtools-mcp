using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to override style setter values
/// </summary>
public class OverrideStyleSetterTool : PipeConnectedToolBase
{
    public OverrideStyleSetterTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var propertyName = ParseStringParam(arguments, "propertyName");
        var value = ParseStringParam(arguments, "value");

        if (string.IsNullOrEmpty(propertyName))
            return CreateMissingParamError("propertyName");

        if (value == null)
            return CreateMissingParamError("value");

        return await SendInspectorRequestAsync(processId, "override_style_setter",
            new { elementId, propertyName, value }, cancellationToken);
    }
}
