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
        var (processId, _, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;
        var compact = arguments.HasValue
            && arguments.Value.TryGetProperty("compact", out var compactProperty)
            && compactProperty.ValueKind == JsonValueKind.True;

        var elements = BatchQueryArgumentParser.ParseElementTargets(arguments, "elementId", "elementIds");
        if (elements.Error != null) return elements.Error;

        var properties = BatchQueryArgumentParser.ParseStringTargets(arguments, "propertyName", "propertyNames", requireAtLeastOne: true);
        if (properties.Error != null) return properties.Error;

        return await BatchQueryExecutor.ExecuteAsync(
            elements.Targets,
            properties.Targets,
            (elementId, propertyName, ct) => SendInspectorRequestAsync(
                processId,
                "get_dp_value_source",
                new { elementId, propertyName, compact },
                ct),
            cancellationToken);
    }
}
