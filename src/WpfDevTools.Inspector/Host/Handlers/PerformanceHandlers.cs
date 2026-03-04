using System.Text.Json;
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
            "get_render_stats" => await HandleGetRenderStatsAsync(@params, cancellationToken).ConfigureAwait(false),
            "find_binding_leaks" => await HandleFindBindingLeaksAsync(@params, cancellationToken).ConfigureAwait(false),
            "measure_element_render_time" => await HandleMeasureElementRenderTimeAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_visual_count" => await HandleGetVisualCountAsync(@params, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetRenderStatsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
            _performanceAnalyzer.GetRenderStats(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleFindBindingLeaksAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var threshold = ParameterHelpers.GetIntParam(@params, "threshold") ?? InspectorConstants.Defaults.BindingLeakThreshold;

        return await Task.Run(() =>
            _performanceAnalyzer.FindBindingLeaks(threshold), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleMeasureElementRenderTimeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _performanceAnalyzer.MeasureElementRenderTime(elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetVisualCountAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _performanceAnalyzer.GetVisualCount(elementId), cancellationToken).ConfigureAwait(false);
    }
}
