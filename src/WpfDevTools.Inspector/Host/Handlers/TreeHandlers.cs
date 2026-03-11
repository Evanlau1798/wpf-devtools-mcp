using System.Text.Json;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host;
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
    /// Initializes a tree request handler with the required analyzers and utilities.
    /// </summary>
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
    /// Gets the inspector method names supported by this handler.
    /// </summary>
    public IEnumerable<string> GetSupportedMethods()
    {
        return new[]
        {
            "get_visual_tree",
            "get_logical_tree",
            "compare_trees",
            "serialize_to_xaml",
            "get_namescope",
            "get_template_tree",
            "get_windows"
        };
    }

    /// <summary>
    /// Handles a tree-related inspector request.
    /// </summary>
    public async Task<object> HandleAsync(string method, JsonElement? @params, CancellationToken cancellationToken)
    {
        return method switch
        {
            "get_visual_tree" => await HandleGetVisualTreeAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_logical_tree" => await HandleGetLogicalTreeAsync(@params, cancellationToken).ConfigureAwait(false),
            "compare_trees" => await HandleCompareTreesAsync(@params, cancellationToken).ConfigureAwait(false),
            "serialize_to_xaml" => await HandleSerializeToXamlAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_namescope" => await HandleGetNameScopeAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_template_tree" => await HandleGetTemplateTreeAsync(@params, cancellationToken).ConfigureAwait(false),
            "get_windows" => await HandleGetWindowsAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported method: {method}")
        };
    }

    private Task<object> HandleGetVisualTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = ParseTreeOptions(@params);
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        return Task.FromResult(_visualTreeAnalyzer.GetVisualTreeWithOptions(options, elementId));
    }

    private Task<object> HandleGetLogicalTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = ParseTreeOptions(@params);
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        return Task.FromResult(_logicalTreeAnalyzer.GetLogicalTreeWithOptions(options, elementId));
    }

    private async Task<object> HandleCompareTreesAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _visualTreeAnalyzer.CompareTree(elementId), cancellationToken).ConfigureAwait(false);
    }

    private Task<object> HandleSerializeToXamlAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return Task.FromResult<object>(ToolErrorFactory.ElementNotFound(elementId));
        }

        var dispatcher = element.Dispatcher ?? System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return Task.FromResult<object>(ToolErrorFactory.ElementNotFound(elementId));
        }

        object SerializeElement() => new
        {
            success = true,
            xaml = _xamlSerializer.SerializeToXaml(element)
        };

        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(SerializeElement());
        }

        return Task.FromResult(dispatcher.Invoke(
            SerializeElement,
            System.Windows.Threading.DispatcherPriority.Normal));
    }

    private async Task<object> HandleGetNameScopeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _visualTreeAnalyzer.GetNameScope(elementId), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetTemplateTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var depth = ParameterHelpers.GetIntParam(@params, "depth");

        return await Task.Run(() =>
            _visualTreeAnalyzer.GetTemplateTree(elementId, depth), cancellationToken).ConfigureAwait(false);
    }

    private Task<object> HandleGetWindowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var windows = _elementFinder.GetWindows();
        return Task.FromResult<object>(new
        {
            success = true,
            windowCount = windows.Count,
            windows = windows.Select(w => new Dictionary<string, object?>
            {
                ["index"] = w.Index,
                ["title"] = w.Title,
                ["type"] = w.Type,
                ["isActive"] = w.IsActive,
                ["isVisible"] = w.IsVisible,
                ["isMainWindow"] = w.IsMainWindow,
                ["elementId"] = w.ElementId
            }).ToList()
        });
    }

    private static TreeTraversalOptions ParseTreeOptions(JsonElement? @params)
    {
        var depth = ParameterHelpers.GetIntParam(@params, "depth");
        var compact = ParameterHelpers.GetBoolParam(@params, "compact");
        var summaryOnly = ParameterHelpers.GetBoolParam(@params, "summaryOnly");
        var maxNodes = ParameterHelpers.GetIntParam(@params, "maxNodes");
        var maxChildrenPerNode = ParameterHelpers.GetIntParam(@params, "maxChildrenPerNode");

        return TreeTraversalOptions.Create(depth, compact, summaryOnly, maxNodes, maxChildrenPerNode);
    }
}
