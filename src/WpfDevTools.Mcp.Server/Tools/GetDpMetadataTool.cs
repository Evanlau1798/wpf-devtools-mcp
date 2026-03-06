using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get DependencyProperty metadata
/// </summary>
public sealed class GetDpMetadataTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetDpMetadataTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetDpMetadataTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_dp_metadata tool to retrieve DependencyProperty metadata
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId, elementId, and propertyName</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing DependencyProperty metadata or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return error;
        var propertyName = ParseStringParam(arguments, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            return CreateMissingParamError("propertyName");

        return await SendInspectorRequestAsync(processId, "get_dp_metadata",
            new { elementId, propertyName }, cancellationToken);
    }
}
