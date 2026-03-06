using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Generic pipe-connected tool that delegates to the Inspector via Named Pipes.
/// Used for tools that simply forward requests without custom logic.
/// </summary>
public class GenericPipeTool : PipeConnectedToolBase
{
    private readonly string _method;
    private readonly Func<JsonElement?, (int processId, object? parameters, object? error)> _paramExtractor;

    /// <summary>
    /// Initializes a new instance of the GenericPipeTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    /// <param name="method">Inspector method name to invoke</param>
    /// <param name="paramExtractor">Optional custom parameter extractor function</param>
    public GenericPipeTool(
        SessionManager sessionManager,
        string method,
        Func<JsonElement?, (int processId, object? parameters, object? error)>? paramExtractor = null)
        : base(sessionManager)
    {
        _method = method;
        _paramExtractor = paramExtractor ?? DefaultParamExtractor;
    }

    /// <summary>
    /// Execute the generic tool by forwarding request to Inspector
    /// </summary>
    /// <param name="arguments">JSON arguments to pass to Inspector</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result from Inspector or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, parameters, error) = _paramExtractor(arguments);
        if (error != null) return error;

        return await SendInspectorRequestAsync(processId, _method, parameters, cancellationToken).ConfigureAwait(false);
    }

    private static (int processId, object? parameters, object? error) DefaultParamExtractor(JsonElement? arguments)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return (-1, null, error);
        return (processId, new { elementId }, null);
    }

    /// <summary>
    /// Parameter extractor for tools that require elementId and propertyName.
    /// Used by: get_binding_value_chain, get_dp_value_source, etc.
    /// </summary>
    public static (int processId, object? parameters, object? error) ExtractElementAndPropertyParams(JsonElement? arguments)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return (-1, null, error);

        var propertyName = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "propertyName");
        if (string.IsNullOrEmpty(propertyName))
            return (-1, null, CreateMissingParamError("propertyName"));

        return (processId, new { elementId, propertyName }, null);
    }

    /// <summary>
    /// Parameter extractor for tools that require elementId, propertyName, and value.
    /// Used by: modify_viewmodel, set_dp_value, etc.
    /// </summary>
    public static (int processId, object? parameters, object? error) ExtractElementPropertyAndValueParams(JsonElement? arguments)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return (-1, null, error);

        var propertyName = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "propertyName");
        if (string.IsNullOrEmpty(propertyName))
            return (-1, null, CreateMissingParamError("propertyName"));

        var value = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "value");
        if (string.IsNullOrEmpty(value))
            return (-1, null, CreateMissingParamError("value"));

        return (processId, new { elementId, propertyName, value }, null);
    }

    /// <summary>
    /// Parameter extractor for highlight_element tool.
    /// Requires processId, optional elementId, color, and duration.
    /// </summary>
    public static (int processId, object? parameters, object? error) ExtractHighlightElementParams(JsonElement? arguments)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null) return (-1, null, error);

        var color = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "color");
        var duration = WpfDevTools.Shared.Utilities.ParameterParser.ParseIntParam(arguments, "duration");

        return (processId, new { elementId, color, duration }, null);
    }

    /// <summary>
    /// Parameter extractor for drag_and_drop tool.
    /// Requires processId, sourceElementId, and targetElementId.
    /// </summary>
    public static (int processId, object? parameters, object? error) ExtractDragAndDropParams(JsonElement? arguments)
    {
        var (processId, _, error) = ParseCommonParams(arguments);
        if (error != null) return (-1, null, error);

        var sourceElementId = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "sourceElementId");
        if (string.IsNullOrEmpty(sourceElementId))
            return (-1, null, CreateMissingParamError("sourceElementId"));

        var targetElementId = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "targetElementId");
        if (string.IsNullOrEmpty(targetElementId))
            return (-1, null, CreateMissingParamError("targetElementId"));

        return (processId, new { sourceElementId, targetElementId }, null);
    }
}
