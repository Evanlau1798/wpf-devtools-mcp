using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to clear DependencyProperty local value
/// </summary>
public sealed class ClearDpValueTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the ClearDpValueTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public ClearDpValueTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the clear_dp_value tool to clear a DependencyProperty local value
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, elementId, and propertyName</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result indicating success or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var propertyName = ParseStringParam(arguments, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            return CreateMissingParamError("propertyName");

        var requestedInput = new { elementId, propertyName };
        var result = await SendInspectorRequestAsync(
            processId,
            "clear_dp_value",
            requestedInput,
            cancellationToken);

        return AddSuccessMetadata(
            result,
            requestedInput,
            "Runtime-only mutation. Use the observed old/new values for manual restore if later steps depend on the previous local value.");
    }
}
