using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles Performance diagnostics related requests
/// </summary>
public class PerformanceHandlers : IRequestHandler
{
    private readonly PerformanceAnalyzer _performanceAnalyzer;

    /// <summary>
    /// Create a new PerformanceHandlers instance
    /// </summary>
    /// <param name="performanceAnalyzer">Performance analyzer for diagnostics</param>
    public PerformanceHandlers(PerformanceAnalyzer performanceAnalyzer)
    {
        _performanceAnalyzer = performanceAnalyzer;
    }

    /// <summary>
    /// Get list of supported method names
    /// </summary>
    /// <returns>Enumerable of method names this handler supports</returns>
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
        var samplingDurationMs = ParameterHelpers.GetIntParam(@params, "samplingDurationMs");

        return await Task.Run(() =>
            _performanceAnalyzer.FindBindingLeaks(threshold, samplingDurationMs), cancellationToken).ConfigureAwait(false);
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
