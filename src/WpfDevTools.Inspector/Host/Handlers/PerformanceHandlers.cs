using System.Text.Json;
using System.Windows;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles Performance diagnostics related requests
/// </summary>
public class PerformanceHandlers : IRequestHandler
{
    private readonly PerformanceAnalyzer _performanceAnalyzer;

    public PerformanceHandlers(PerformanceAnalyzer performanceAnalyzer)
    {
        _performanceAnalyzer = performanceAnalyzer;
    }

    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "get_render_stats",
            "find_binding_leaks",
            "measure_element_render_time",
            "get_visual_count"
        };
    }

    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "get_render_stats" => await HandleGetRenderStatsAsync(@params, cancellationToken),
            "find_binding_leaks" => await HandleFindBindingLeaksAsync(@params, cancellationToken),
            "measure_element_render_time" => await HandleMeasureElementRenderTimeAsync(@params, cancellationToken),
            "get_visual_count" => await HandleGetVisualCountAsync(@params, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetRenderStatsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _performanceAnalyzer.GetRenderStats()));
    }

    private async Task<object> HandleFindBindingLeaksAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var threshold = ParameterHelpers.GetIntParam(@params, "threshold") ?? InspectorConstants.Defaults.BindingLeakThreshold;

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _performanceAnalyzer.FindBindingLeaks(threshold)));
    }

    private async Task<object> HandleMeasureElementRenderTimeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _performanceAnalyzer.MeasureElementRenderTime(elementId)));
    }

    private async Task<object> HandleGetVisualCountAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _performanceAnalyzer.GetVisualCount(elementId)));
    }
}
