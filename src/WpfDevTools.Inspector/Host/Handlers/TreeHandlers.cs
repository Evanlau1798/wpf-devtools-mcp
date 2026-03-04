using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Host.Handlers;

/// <summary>
/// Handles Visual Tree and Logical Tree related requests
/// </summary>
public class TreeHandlers : IRequestHandler
{
    private readonly VisualTreeAnalyzer _visualTreeAnalyzer;
    private readonly LogicalTreeAnalyzer _logicalTreeAnalyzer;
    private readonly XamlSerializer _xamlSerializer;
    private readonly ElementFinder _elementFinder;

    public TreeHandlers(
        VisualTreeAnalyzer visualTreeAnalyzer,
        LogicalTreeAnalyzer logicalTreeAnalyzer,
        XamlSerializer xamlSerializer,
        ElementFinder elementFinder)
    {
        _visualTreeAnalyzer = visualTreeAnalyzer;
        _logicalTreeAnalyzer = logicalTreeAnalyzer;
        _xamlSerializer = xamlSerializer;
        _elementFinder = elementFinder;
    }

    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "get_visual_tree",
            "get_logical_tree",
            "compare_trees",
            "serialize_to_xaml",
            "get_namescope"
        };
    }

    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "get_visual_tree" => await HandleGetVisualTreeAsync(@params, cancellationToken),
            "get_logical_tree" => await HandleGetLogicalTreeAsync(@params, cancellationToken),
            "compare_trees" => await HandleCompareTreesAsync(@params, cancellationToken),
            "serialize_to_xaml" => await HandleSerializeToXamlAsync(@params, cancellationToken),
            "get_namescope" => await HandleGetNameScopeAsync(@params, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetVisualTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var depth = ParameterHelpers.GetIntParam(@params, "depth");

        return await Task.Run(() =>
            _visualTreeAnalyzer.GetVisualTree(depth, elementId), cancellationToken);
    }

    private async Task<object> HandleGetLogicalTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var depth = ParameterHelpers.GetIntParam(@params, "depth");

        return await Task.Run(() =>
            _logicalTreeAnalyzer.GetLogicalTree(depth, elementId), cancellationToken);
    }

    private async Task<object> HandleCompareTreesAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _visualTreeAnalyzer.CompareTree(elementId), cancellationToken);
    }

    private async Task<object> HandleSerializeToXamlAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
        {
            // XamlSerializer is a utility, not an analyzer with dispatch support.
            // Element lookup and XAML serialization must run on the UI thread.
            return System.Windows.Application.Current.Dispatcher.Invoke<object>(() =>
            {
                var element = elementId == null
                    ? _elementFinder.GetRootElement()
                    : _elementFinder.FindById(elementId);

                if (element == null)
                {
                    return new { success = false, error = "Element not found" };
                }

                var xaml = _xamlSerializer.SerializeToXaml(element);
                return new { success = true, xaml };
            });
        }, cancellationToken);
    }

    private async Task<object> HandleGetNameScopeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _visualTreeAnalyzer.GetNameScope(elementId), cancellationToken);
    }
}
