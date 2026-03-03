using System.Text.Json;
using System.Windows;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles Layout related requests
/// </summary>
public class LayoutHandlers : IRequestHandler
{
    private readonly LayoutAnalyzer _layoutAnalyzer;

    public LayoutHandlers(LayoutAnalyzer layoutAnalyzer)
    {
        _layoutAnalyzer = layoutAnalyzer;
    }

    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "get_layout_info",
            "get_clipping_info",
            "highlight_element",
            "invalidate_layout"
        };
    }

    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "get_layout_info" => await HandleGetLayoutInfoAsync(@params, cancellationToken),
            "get_clipping_info" => await HandleGetClippingInfoAsync(@params, cancellationToken),
            "highlight_element" => await HandleHighlightElementAsync(@params, cancellationToken),
            "invalidate_layout" => await HandleInvalidateLayoutAsync(@params, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetLayoutInfoAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _layoutAnalyzer.GetLayoutInfo(elementId)));
    }

    private async Task<object> HandleGetClippingInfoAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _layoutAnalyzer.GetClippingInfo(elementId)));
    }

    private async Task<object> HandleHighlightElementAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var color = ParameterHelpers.GetStringParam(@params, "color") ?? InspectorConstants.Colors.Red;
        var duration = ParameterHelpers.GetIntParam(@params, "duration") ?? InspectorConstants.Defaults.HighlightDuration;

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _layoutAnalyzer.HighlightElement(elementId, color, duration)));
    }

    private async Task<object> HandleInvalidateLayoutAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _layoutAnalyzer.InvalidateLayout(elementId)));
    }
}
