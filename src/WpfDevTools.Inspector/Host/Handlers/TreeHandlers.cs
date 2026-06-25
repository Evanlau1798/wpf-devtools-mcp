using System.Text.Json;
using System.Text;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Serialization;

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
    private readonly TimeSpan? _serializeToXamlDispatcherTimeout;
    private readonly int _maxSerializedXamlCharacters;
    private readonly int _maxSerializedXamlUtf8Bytes;

    /// <summary>
    /// Initializes a tree request handler with the required analyzers and utilities.
    /// </summary>
    public TreeHandlers(
        VisualTreeAnalyzer visualTreeAnalyzer,
        LogicalTreeAnalyzer logicalTreeAnalyzer,
        XamlSerializer xamlSerializer,
        ElementFinder elementFinder)
        : this(
            visualTreeAnalyzer,
            logicalTreeAnalyzer,
            xamlSerializer,
            elementFinder,
            serializeToXamlDispatcherTimeout: null,
            maxSerializedXamlCharacters: null,
            maxSerializedXamlUtf8Bytes: null)
    {
    }

    internal TreeHandlers(
        VisualTreeAnalyzer visualTreeAnalyzer,
        LogicalTreeAnalyzer logicalTreeAnalyzer,
        XamlSerializer xamlSerializer,
        ElementFinder elementFinder,
        TimeSpan? serializeToXamlDispatcherTimeout = null,
        int? maxSerializedXamlCharacters = null,
        int? maxSerializedXamlUtf8Bytes = null)
    {
        _visualTreeAnalyzer = visualTreeAnalyzer;
        _logicalTreeAnalyzer = logicalTreeAnalyzer;
        _xamlSerializer = xamlSerializer;
        _elementFinder = elementFinder;
        _serializeToXamlDispatcherTimeout = ValidateTimeout(serializeToXamlDispatcherTimeout);
        _maxSerializedXamlCharacters = ValidatePositiveBudget(
            maxSerializedXamlCharacters,
            XamlSerializer.DefaultMaxSerializedXamlCharacters,
            nameof(maxSerializedXamlCharacters));
        _maxSerializedXamlUtf8Bytes = ValidatePositiveBudget(
            maxSerializedXamlUtf8Bytes,
            XamlSerializer.DefaultMaxSerializedXamlUtf8Bytes,
            nameof(maxSerializedXamlUtf8Bytes));
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

        object SerializeElement()
        {
            try
            {
                var xaml = _xamlSerializer.SerializeToXaml(element, _maxSerializedXamlCharacters);
                var rawXamlByteLength = Encoding.UTF8.GetByteCount(xaml);
                var successResult = new
                {
                    success = true,
                    xaml
                };
                var byteLength = JsonSerializer.SerializeToUtf8Bytes(successResult).Length;
                if (byteLength > _maxSerializedXamlUtf8Bytes)
                {
                    return CreateXamlPayloadTooLargeError(xaml.Length, byteLength, rawXamlByteLength);
                }

                return successResult;
            }
            catch (XamlPayloadTooLargeException ex)
            {
                return CreateXamlPayloadTooLargeError(
                    ex.CharacterCount,
                    byteLength: null,
                    rawXamlByteLength: null);
            }
            catch (XamlSerializationException ex)
            {
                return ToolErrorFactory.XamlSerializationFailed(
                    elementId,
                    element.GetType().Name,
                    ex.ExceptionType);
            }
        }

        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(SerializeElement());
        }

        return Task.FromResult(DispatcherOperationRunner.Invoke(
            dispatcher,
            SerializeElement,
            _serializeToXamlDispatcherTimeout ?? InspectorConfig.UIThreadTimeout,
            cancellationToken,
            "serialize_to_xaml dispatcher operation",
            "serialize_to_xaml dispatcher operation"));
    }

    private async Task<object> HandleGetNameScopeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var maxNodes = ParameterHelpers.GetIntParam(@params, "maxNodes");

        return await Task.Run(() =>
            _visualTreeAnalyzer.GetNameScope(elementId, maxNodes), cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> HandleGetTemplateTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = ParameterHelpers.GetStringParam(@params, "elementId");
        var depth = ParameterHelpers.GetIntParam(@params, "depth");
        var compact = ParameterHelpers.GetBoolParam(@params, "compact");
        var maxNodes = ParameterHelpers.GetIntParam(@params, "maxNodes");
        var maxChildrenPerNode = ParameterHelpers.GetIntParam(@params, "maxChildrenPerNode");
        var options = TreeTraversalOptions.Create(
            depth,
            compact,
            summaryOnly: false,
            maxNodes,
            maxChildrenPerNode);

        return await Task.Run(() =>
            _visualTreeAnalyzer.GetTemplateTreeWithOptions(elementId, options), cancellationToken).ConfigureAwait(false);
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

    private object CreateXamlPayloadTooLargeError(
        int characterCount,
        int? byteLength,
        int? rawXamlByteLength)
    {
        return ToolErrorFactory.PayloadTooLarge(
            "Serialized XAML exceeds the serialize_to_xaml payload budget.",
            "Target a smaller element, use get_ui_summary or get_element_snapshot first, or inspect a narrower subtree before retrying serialize_to_xaml.",
            new
            {
                characterCount,
                byteLength,
                rawXamlByteLength,
                maxCharacterCount = _maxSerializedXamlCharacters,
                maxByteLength = _maxSerializedXamlUtf8Bytes,
                messageFramingMaxByteLength = MessageFraming.MaxMessageSizeBytes
            });
    }

    private static TimeSpan? ValidateTimeout(TimeSpan? timeout)
    {
        if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Dispatcher timeout must be positive.");
        }

        return timeout;
    }

    private static int ValidatePositiveBudget(int? value, int defaultValue, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Serialized XAML budget must be positive.");
        }

        return value ?? defaultValue;
    }
}
