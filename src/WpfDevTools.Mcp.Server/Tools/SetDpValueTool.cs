using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to set DependencyProperty value
/// </summary>
public sealed class SetDpValueTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the SetDpValueTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public SetDpValueTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the set_dp_value tool to set a DependencyProperty value
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, elementId, propertyName, and value</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result indicating success or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var propertyName = ParseStringParam(arguments, "propertyName");
        var value = WpfDevTools.Shared.Utilities.ParameterParser.ParseJsonParam(arguments, "value");

        if (string.IsNullOrEmpty(propertyName))
            return CreateMissingParamError("propertyName");

        if (value == null)
            return CreateMissingParamError("value");

        var requestedInput = new { elementId, propertyName, value = value.Value };
        var result = await SendInspectorRequestAsync(
            processId,
            "set_dp_value",
            requestedInput,
            cancellationToken);

        return AddSuccessMetadata(
            result,
            requestedInput,
            "Runtime-only mutation. Capture oldValue/newValue if you need manual restore after verification.");
    }
}
