using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to override style setter values
/// </summary>
public sealed class OverrideStyleSetterTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the OverrideStyleSetterTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public OverrideStyleSetterTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the override_style_setter tool to modify style setter values at runtime
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, elementId, propertyName, and value</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result indicating success or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;
        var (detailMode, detailError) = ParseMutationDetailMode(arguments);
        if (detailError != null) return detailError;
        var propertyName = ParseStringParam(arguments, "propertyName");
        var value = WpfDevTools.Shared.Utilities.ParameterParser.ParseJsonParam(arguments, "value");

        if (elementId == null)
            return CreateMissingParamError("elementId");

        if (string.IsNullOrEmpty(propertyName))
            return CreateMissingParamError("propertyName");

        if (value == null)
            return CreateMissingParamError("value");

        var requestedInput = new { elementId, propertyName, value = value.Value };
        var result = await SendInspectorRequestAsync(
            processId,
            "override_style_setter",
            requestedInput,
            cancellationToken);

        return AddSuccessMetadata(
            result,
            requestedInput,
            "Runtime-only style override. Record the observed style values before using this in demos, troubleshooting, or regression flows.",
            detailMode: detailMode);
    }
}
