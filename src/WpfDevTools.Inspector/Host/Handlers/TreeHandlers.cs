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

    /// <summary>
    /// Create a new TreeHandlers instance
    /// </summary>
    /// <param name="visualTreeAnalyzer">Visual tree analyzer</param>
    /// <param name="logicalTreeAnalyzer">Logical tree analyzer</param>
    /// <param name="xamlSerializer">XAML serializer</param>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
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

    /// <summary>
    /// Get list of supported method names
    /// </summary>
    /// <returns>Enumerable of method names this handler supports</returns>
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
            "get_visual_tree" => await HandleGetVisualTreeAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_logical_tree" => await HandleGetLogicalTreeAsync(@params, cancellationToken).ConfigureAwait(false),
            "compare_trees" => await HandleCompareTreesAsync(@params, cancellationToken).ConfigureAwait(false),
            "serialize_to_xaml" => await HandleSerializeToXamlAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_namescope" => await HandleGetNameScopeAsync(@params, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private async Task<object> HandleGetVisualTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var depth = ParameterHelpers.GetIntParam(@params, "depth");

        return await Task.Run(() =>
            _visualTreeAnalyzer.GetVisualTree(depth, elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetLogicalTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var depth = ParameterHelpers.GetIntParam(@params, "depth");

        return await Task.Run(() =>
            _logicalTreeAnalyzer.GetLogicalTree(depth, elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleCompareTreesAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _visualTreeAnalyzer.CompareTree(elementId), cancellationToken).ConfigureAwait(false);
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
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetNameScopeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _visualTreeAnalyzer.GetNameScope(elementId), cancellationToken).ConfigureAwait(false);
    }
}
