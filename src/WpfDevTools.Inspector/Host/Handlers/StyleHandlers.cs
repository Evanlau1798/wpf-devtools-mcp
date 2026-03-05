using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles Style and Template related requests
/// </summary>
public class StyleHandlers : IRequestHandler
{
    private readonly StyleAnalyzer _styleAnalyzer;

    /// <summary>
    /// Create a new StyleHandlers instance
    /// </summary>
    /// <param name="styleAnalyzer">Style analyzer for style and template operations</param>
    public StyleHandlers(StyleAnalyzer styleAnalyzer)
    {
        _styleAnalyzer = styleAnalyzer;
    }

    /// <summary>
    /// Get list of supported method names
    /// </summary>
    /// <returns>Enumerable of method names this handler supports</returns>
    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "get_applied_styles",
            "get_triggers",
            "get_resource_chain",
            "override_style_setter"
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
            "get_applied_styles" => await HandleGetAppliedStylesAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_triggers" => await HandleGetTriggersAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_resource_chain" => await HandleGetResourceChainAsync(@params, cancellationToken).ConfigureAwait(false),
            "override_style_setter" => await HandleOverrideStyleSetterAsync(@params, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetAppliedStylesAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _styleAnalyzer.GetAppliedStyles(elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetTriggersAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _styleAnalyzer.GetTriggers(elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetResourceChainAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var resourceKey = ParameterHelpers.GetStringParam(@params, "resourceKey");

        if (string.IsNullOrEmpty(resourceKey))
            throw new ArgumentException("Missing required parameter: resourceKey");

        return await Task.Run(() =>
            _styleAnalyzer.GetResourceChain(elementId, resourceKey!), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleOverrideStyleSetterAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var propertyName = ParameterHelpers.GetStringParam(@params, "propertyName");
        var value = ParameterHelpers.GetObjectParam<object>(@params, "value");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        if (value == null)
            throw new ArgumentException("Missing required parameter: value");

        return await Task.Run(() =>
            _styleAnalyzer.OverrideStyleSetter(elementId, propertyName!, value), cancellationToken).ConfigureAwait(false);
    }
}
