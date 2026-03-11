using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get validation errors from WPF application
/// </summary>
public sealed class GetValidationErrorsTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the GetValidationErrorsTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public GetValidationErrorsTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the get_validation_errors tool to retrieve validation errors
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing validation error information or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;
        var elements = BatchQueryArgumentParser.ParseElementTargets(arguments, "elementId", "elementIds");
        if (elements.Error != null) return elements.Error;

        return await BatchQueryExecutor.ExecuteAsync(
            elements.Targets,
            new string?[] { null },
            (elementId, _, ct) => SendInspectorRequestAsync(
                processId,
                "get_validation_errors",
                new { elementId },
                ct),
            cancellationToken);
    }
}
