using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles Layout related requests
/// </summary>
public class LayoutHandlers : IRequestHandler
{
    private readonly LayoutAnalyzer _layoutAnalyzer;

    /// <summary>
    /// Create a new LayoutHandlers instance
    /// </summary>
    /// <param name="layoutAnalyzer">Layout analyzer for layout operations</param>
    public LayoutHandlers(LayoutAnalyzer layoutAnalyzer)
    {
        _layoutAnalyzer = layoutAnalyzer;
    }

    /// <summary>
    /// Get list of supported method names
    /// </summary>
    /// <returns>Enumerable of method names this handler supports</returns>
    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "get_layout_info",
            "get_clipping_info",
            "diagnose_visibility",
            "highlight_element",
            "invalidate_layout"
        };
    }

    /// <summary>
    /// Handle an Inspector request
    /// </summary>
    /// <param name="method">Method name to execute</param>
    /// <param name="params">JSON parameters for the method</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result object from method execution</returns>
    /// <exception cref="InvalidOperationException">Thrown when method is not supported</exception>
    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "get_layout_info" => await HandleGetLayoutInfoAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_clipping_info" => await HandleGetClippingInfoAsync(@params, cancellationToken).ConfigureAwait(false),
            "diagnose_visibility" => await HandleDiagnoseVisibilityAsync(@params, cancellationToken).ConfigureAwait(false),
            "highlight_element" => await HandleHighlightElementAsync(@params, cancellationToken).ConfigureAwait(false),
            "invalidate_layout" => await HandleInvalidateLayoutAsync(@params, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetLayoutInfoAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _layoutAnalyzer.GetLayoutInfo(elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetClippingInfoAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _layoutAnalyzer.GetClippingInfo(elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleDiagnoseVisibilityAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _layoutAnalyzer.DiagnoseVisibility(elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleHighlightElementAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var color = ParameterHelpers.GetStringParam(@params, "color") ?? InspectorConstants.Colors.Red;
        var duration = ParameterHelpers.GetIntParam(@params, "duration") ?? InspectorConstants.Defaults.HighlightDuration;

        return await Task.Run(() =>
            _layoutAnalyzer.HighlightElement(elementId, color, duration), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleInvalidateLayoutAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _layoutAnalyzer.InvalidateLayout(elementId), cancellationToken).ConfigureAwait(false);
    }
}
