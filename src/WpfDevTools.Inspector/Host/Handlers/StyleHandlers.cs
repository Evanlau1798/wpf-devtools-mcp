using System.Text.Json;
using System.Windows;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles Style and Template related requests
/// </summary>
public class StyleHandlers : IRequestHandler
{
    private readonly StyleAnalyzer _styleAnalyzer;

    public StyleHandlers(StyleAnalyzer styleAnalyzer)
    {
        _styleAnalyzer = styleAnalyzer;
    }

    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "get_applied_styles",
            "get_triggers",
            "get_template_tree",
            "get_resource_chain",
            "override_style_setter"
        };
    }

    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "get_applied_styles" => await HandleGetAppliedStylesAsync(@params, cancellationToken),
            "get_triggers" => await HandleGetTriggersAsync(@params, cancellationToken),
            "get_template_tree" => await HandleGetTemplateTreeAsync(@params, cancellationToken),
            "get_resource_chain" => await HandleGetResourceChainAsync(@params, cancellationToken),
            "override_style_setter" => await HandleOverrideStyleSetterAsync(@params, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetAppliedStylesAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _styleAnalyzer.GetAppliedStyles(elementId)));
    }

    private async Task<object> HandleGetTriggersAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _styleAnalyzer.GetTriggers(elementId)));
    }

    private async Task<object> HandleGetTemplateTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _styleAnalyzer.GetTemplateTree(elementId)));
    }

    private async Task<object> HandleGetResourceChainAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var resourceKey = ParameterHelpers.GetStringParam(@params, "resourceKey");

        if (string.IsNullOrEmpty(resourceKey))
            throw new ArgumentException("Missing required parameter: resourceKey");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _styleAnalyzer.GetResourceChain(elementId, resourceKey!)));
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
            Application.Current.Dispatcher.Invoke(() =>
                _styleAnalyzer.OverrideStyleSetter(elementId, propertyName!, value)));
    }
}
