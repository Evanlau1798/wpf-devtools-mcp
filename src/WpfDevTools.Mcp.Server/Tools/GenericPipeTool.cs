using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Generic pipe-connected tool that delegates to the Inspector via Named Pipes.
/// Used for tools that simply forward requests without custom logic.
/// </summary>
public sealed class GenericPipeTool : PipeConnectedToolBase
{
    private readonly string _method;
    private readonly Func<SessionManager, JsonElement?, (int processId, object? parameters, object? error)> _paramExtractor;
    private readonly Func<object, object?, MutationDetailMode, object>? _successMetadataAugmenter;

    /// <summary>
    /// Initializes a new instance of the GenericPipeTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    /// <param name="method">Inspector method name to invoke</param>
    /// <param name="paramExtractor">Optional custom parameter extractor function</param>
    /// <param name="successMetadataAugmenter">Optional success-response augmenter for adding stable metadata to mutation-style results.</param>
    public GenericPipeTool(
        SessionManager sessionManager,
        string method,
        Func<SessionManager, JsonElement?, (int processId, object? parameters, object? error)>? paramExtractor = null,
        Func<object, object?, MutationDetailMode, object>? successMetadataAugmenter = null)
        : base(sessionManager)
    {
        _method = method;
        _paramExtractor = paramExtractor ?? DefaultParamExtractor;
        _successMetadataAugmenter = successMetadataAugmenter;
    }

    /// <summary>
    /// Execute the generic tool by forwarding request to Inspector
    /// </summary>
    /// <param name="arguments">JSON arguments to pass to Inspector</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result from Inspector or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, parameters, error) = _paramExtractor(_sessionManager, arguments);
        if (error != null) return error;
        var detailMode = MutationDetailMode.Compact;
        if (_successMetadataAugmenter != null)
        {
            var (parsedMode, detailError) = ParseMutationDetailMode(arguments);
            if (detailError != null) return detailError;
            detailMode = parsedMode;
        }

        var result = await SendInspectorRequestAsync(processId, _method, parameters, cancellationToken).ConfigureAwait(false);
        return _successMetadataAugmenter?.Invoke(result, parameters, detailMode) ?? result;
    }

    public static object AugmentModifyViewModelResult(object result, object? requestedInput, MutationDetailMode detailMode)
    {
        return AddSuccessMetadata(
            result,
            requestedInput ?? new { },
            "Runtime-only ViewModel mutation. UI refresh still depends on INotifyPropertyChanged and any binding-side validation.",
            usedFallback: false,
            detailMode: detailMode);
    }

    private static (int processId, object? parameters, object? error) DefaultParamExtractor(
        SessionManager sessionManager,
        JsonElement? arguments)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, sessionManager);
        if (error != null) return (-1, null, error);
        return (processId, new { elementId }, null);
    }

    /// <summary>
    /// Parameter extractor for tools that require elementId and propertyName.
    /// Used by: get_binding_value_chain, get_dp_value_source, etc.
    /// </summary>
    public static (int processId, object? parameters, object? error) ExtractElementAndPropertyParams(
        SessionManager sessionManager,
        JsonElement? arguments)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, sessionManager);
        if (error != null) return (-1, null, error);

        var propertyName = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "propertyName");
        if (string.IsNullOrEmpty(propertyName))
            return (-1, null, CreateMissingParamError("propertyName"));

        var direction = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "direction");

        return (processId, new { elementId, propertyName, direction }, null);
    }

    /// <summary>
    /// Parameter extractor for tools that require elementId, propertyName, and value.
    /// Used by: modify_viewmodel, set_dp_value, etc.
    /// </summary>
    public static (int processId, object? parameters, object? error) ExtractElementPropertyAndValueParams(
        SessionManager sessionManager,
        JsonElement? arguments)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, sessionManager);
        if (error != null) return (-1, null, error);

        var propertyName = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "propertyName");
        if (string.IsNullOrEmpty(propertyName))
            return (-1, null, CreateMissingParamError("propertyName"));

        var value = WpfDevTools.Shared.Utilities.ParameterParser.ParseJsonParam(arguments, "value");
        if (value == null)
            return (-1, null, CreateMissingParamError("value"));

        return (processId, new { elementId, propertyName, value = value.Value }, null);
    }

    /// <summary>
    /// Parameter extractor for highlight_element tool.
    /// Requires processId and elementId; accepts optional color and duration.
    /// </summary>
    public static (int processId, object? parameters, object? error) ExtractHighlightElementParams(
        SessionManager sessionManager,
        JsonElement? arguments)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, sessionManager);
        if (error != null) return (-1, null, error);

        if (elementId == null)
            return (-1, null, CreateMissingParamError("elementId"));

        var color = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "color");
        var duration = WpfDevTools.Shared.Utilities.ParameterParser.ParseIntParam(arguments, "duration");

        return (processId, new { elementId, color, duration }, null);
    }

    /// <summary>
    /// Parameter extractor for drag_and_drop tool.
    /// Requires processId, sourceElementId, and targetElementId.
    /// </summary>
    public static (int processId, object? parameters, object? error) ExtractDragAndDropParams(
        SessionManager sessionManager,
        JsonElement? arguments)
    {
        var (processId, _, error) = ParseCommonParams(arguments, sessionManager);
        if (error != null) return (-1, null, error);

        var sourceElementId = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "sourceElementId");
        if (string.IsNullOrEmpty(sourceElementId))
            return (-1, null, CreateMissingParamError("sourceElementId"));

        var targetElementId = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "targetElementId");
        if (string.IsNullOrEmpty(targetElementId))
            return (-1, null, CreateMissingParamError("targetElementId"));

        var dataFormat = WpfDevTools.Shared.Utilities.ParameterParser.ParseStringParam(arguments, "dataFormat");

        return (processId, new { sourceElementId, targetElementId, dataFormat }, null);
    }
}
