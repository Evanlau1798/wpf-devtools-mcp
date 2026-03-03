using System.Text.Json;
using System.Windows;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Host;

/// <summary>
/// Dispatches incoming requests to appropriate handlers
/// </summary>
public class RequestDispatcher
{
    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object>>> _handlers;
    private readonly VisualTreeAnalyzer _visualTreeAnalyzer;
    private readonly BindingAnalyzer _bindingAnalyzer;
    private readonly ElementFinder _elementFinder;
    private readonly LogicalTreeAnalyzer _logicalTreeAnalyzer;
    private readonly MvvmAnalyzer _mvvmAnalyzer;
    private readonly DependencyPropertyAnalyzer _dependencyPropertyAnalyzer;
    private readonly LayoutAnalyzer _layoutAnalyzer;
    private readonly InteractionAnalyzer _interactionAnalyzer;
    private readonly StyleAnalyzer _styleAnalyzer;
    private readonly EventAnalyzer _eventAnalyzer;
    private readonly PerformanceAnalyzer _performanceAnalyzer;
    private readonly XamlSerializer _xamlSerializer;

    public RequestDispatcher()
    {
        // Initialize shared utilities
        _elementFinder = new ElementFinder();
        _xamlSerializer = new XamlSerializer();

        // Initialize analyzers
        _visualTreeAnalyzer = new VisualTreeAnalyzer(_elementFinder);
        _bindingAnalyzer = new BindingAnalyzer();
        _logicalTreeAnalyzer = new LogicalTreeAnalyzer(_elementFinder);
        _mvvmAnalyzer = new MvvmAnalyzer(_elementFinder);
        _dependencyPropertyAnalyzer = new DependencyPropertyAnalyzer(_elementFinder);
        _layoutAnalyzer = new LayoutAnalyzer(_elementFinder);
        _interactionAnalyzer = new InteractionAnalyzer(_elementFinder);
        _styleAnalyzer = new StyleAnalyzer(_elementFinder);
        _eventAnalyzer = new EventAnalyzer(_elementFinder);
        _performanceAnalyzer = new PerformanceAnalyzer();

        _handlers = new Dictionary<string, Func<JsonElement?, CancellationToken, Task<object>>>
        {
            // Implemented tools
            ["ping"] = HandlePingAsync,
            ["get_visual_tree"] = HandleGetVisualTreeAsync,
            ["get_bindings"] = HandleGetBindingsAsync,
            ["get_binding_errors"] = HandleGetBindingErrorsAsync,
            ["get_datacontext_chain"] = HandleGetDataContextChainAsync,

            // Logical Tree tools
            ["get_logical_tree"] = HandleGetLogicalTreeAsync,
            ["compare_trees"] = HandleCompareTreesAsync,
            ["serialize_to_xaml"] = HandleSerializeToXamlAsync,
            ["get_namescope"] = HandleGetNameScopeAsync,

            // MVVM tools
            ["get_viewmodel"] = HandleGetViewModelAsync,
            ["get_commands"] = HandleGetCommandsAsync,
            ["execute_command"] = HandleExecuteCommandAsync,
            ["get_validation_errors"] = HandleGetValidationErrorsAsync,

            // DependencyProperty tools (Phase 2)
            ["get_dp_value_source"] = HandleGetDpValueSourceAsync,
            ["get_dp_metadata"] = HandleGetDpMetadataAsync,
            ["set_dp_value"] = HandleSetDpValueAsync,
            ["clear_dp_value"] = HandleClearDpValueAsync,
            ["watch_dp_changes"] = HandleWatchDpChangesAsync,

            // Binding Diagnostics tools (Phase 6)
            ["get_binding_value_chain"] = HandleGetBindingValueChainAsync,
            ["force_binding_update"] = HandleForceBindingUpdateAsync,

            // Layout tools (Phase 2)
            ["get_layout_info"] = HandleGetLayoutInfoAsync,
            ["get_clipping_info"] = HandleGetClippingInfoAsync,

            // Interaction tools (Phase 3)
            ["click_element"] = HandleClickElementAsync,
            ["scroll_to_element"] = HandleScrollToElementAsync,
            ["element_screenshot"] = HandleElementScreenshotAsync,

            // Style tools (Phase 3)
            ["get_applied_styles"] = HandleGetAppliedStylesAsync,
            ["get_triggers"] = HandleGetTriggersAsync,
            ["get_template_tree"] = HandleGetTemplateTreeAsync,
            ["get_resource_chain"] = HandleGetResourceChainAsync,
            ["override_style_setter"] = HandleOverrideStyleSetterAsync,

            // Event tools (Phase 4)
            ["trace_routed_events"] = HandleTraceRoutedEventsAsync,
            ["fire_routed_event"] = HandleFireRoutedEventAsync,

            // Performance tools (Phase 4)
            ["get_render_stats"] = HandleGetRenderStatsAsync,
            ["get_visual_count"] = HandleGetVisualCountAsync,
            ["measure_element_render_time"] = HandleMeasureElementRenderTimeAsync,

            // Test
            ["test_slow"] = HandleTestSlowAsync
        };
    }

    /// <summary>
    /// Dispatch request to appropriate handler
    /// </summary>
    public async Task<InspectorResponse> DispatchAsync(
        InspectorRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if method exists
            if (!_handlers.ContainsKey(request.Method))
            {
                return new InspectorResponse
                {
                    Id = request.Id,
                    Result = null,
                    Error = new InspectorError
                    {
                        Code = ErrorCode.MethodNotFound,
                        Message = $"Method not found: {request.Method}",
                        Data = null
                    }
                };
            }

            // Execute handler
            var handler = _handlers[request.Method];
            var result = await handler(request.Params, cancellationToken);

            return new InspectorResponse
            {
                Id = request.Id,
                Result = JsonSerializer.SerializeToElement(result),
                Error = null
            };
        }
        catch (OperationCanceledException)
        {
            return new InspectorResponse
            {
                Id = request.Id,
                Result = null,
                Error = new InspectorError
                {
                    Code = ErrorCode.InternalError,
                    Message = "Request cancelled or timed out",
                    Data = null
                }
            };
        }
        catch (ArgumentException ex)
        {
            return new InspectorResponse
            {
                Id = request.Id,
                Result = null,
                Error = new InspectorError
                {
                    Code = ErrorCode.InvalidParams,
                    Message = ex.Message,
                    Data = null
                }
            };
        }
        catch (Exception ex)
        {
            return new InspectorResponse
            {
                Id = request.Id,
                Result = null,
                Error = new InspectorError
                {
                    Code = ErrorCode.InternalError,
                    Message = ex.Message,
                    Data = null
                }
            };
        }
    }

    private async Task<object> HandlePingAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return new { status = "pong", timestamp = DateTime.UtcNow };
    }

    private async Task<object> HandleTestSlowAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        return new { status = "completed" };
    }

    private async Task<object> HandleGetVisualTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var depth = GetIntParam(@params, "depth");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _visualTreeAnalyzer.GetVisualTree(depth, elementId)));
    }

    private async Task<object> HandleGetBindingsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _bindingAnalyzer.GetBindings(elementId)));
    }

    private async Task<object> HandleGetBindingErrorsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _bindingAnalyzer.GetBindingErrors()));
    }

    private async Task<object> HandleGetDataContextChainAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        if (string.IsNullOrEmpty(elementId))
            throw new ArgumentException("Missing required parameter: elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _bindingAnalyzer.GetDataContextChain(elementId)));
    }

    private async Task<object> HandleGetLogicalTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var depth = GetIntParam(@params, "depth");

        return await Task.Run(() =>
            _logicalTreeAnalyzer.GetLogicalTree(depth, elementId));
    }

    private async Task<object> HandleGetViewModelAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _mvvmAnalyzer.GetViewModel(elementId));
    }

    private async Task<object> HandleGetCommandsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _mvvmAnalyzer.GetCommands(elementId));
    }

    private async Task<object> HandleExecuteCommandAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var commandName = GetStringParam(@params, "commandName");
        var parameter = GetStringParam(@params, "parameter");

        if (string.IsNullOrEmpty(commandName))
            throw new ArgumentException("Missing required parameter: commandName");

        return await Task.Run(() =>
            _mvvmAnalyzer.ExecuteCommand(elementId, commandName!, parameter));
    }

    private async Task<object> HandleGetValidationErrorsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            _mvvmAnalyzer.GetValidationErrors(elementId));
    }

    private async Task<object> HandleGetDpValueSourceAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var propertyName = GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _dependencyPropertyAnalyzer.GetValueSource(propertyName!, elementId)));
    }

    private async Task<object> HandleSetDpValueAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var propertyName = GetStringParam(@params, "propertyName");
        var value = GetObjectParam<object>(@params, "value");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        if (value == null)
            throw new ArgumentException("Missing required parameter: value");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _dependencyPropertyAnalyzer.SetValue(propertyName!, value, elementId)));
    }

    private async Task<object> HandleClearDpValueAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var propertyName = GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _dependencyPropertyAnalyzer.ClearValue(propertyName!, elementId)));
    }

    private async Task<object> HandleGetDpMetadataAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var propertyName = GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _dependencyPropertyAnalyzer.GetMetadata(propertyName!, elementId)));
    }

    private async Task<object> HandleWatchDpChangesAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var propertyName = GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _dependencyPropertyAnalyzer.WatchChanges(propertyName!, elementId)));
    }

    private async Task<object> HandleGetBindingValueChainAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var propertyName = GetStringParam(@params, "propertyName");

        if (string.IsNullOrEmpty(elementId))
            throw new ArgumentException("Missing required parameter: elementId");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke<object>(() =>
            {
                var element = _elementFinder.FindById(elementId!);
                if (element == null)
                {
                    return new { success = false, error = "Element not found" };
                }

                return _bindingAnalyzer.GetBindingValueChain(element, propertyName!);
            }));
    }

    private async Task<object> HandleForceBindingUpdateAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var propertyName = GetStringParam(@params, "propertyName");
        var direction = GetStringParam(@params, "direction") ?? "Source";

        if (string.IsNullOrEmpty(elementId))
            throw new ArgumentException("Missing required parameter: elementId");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke<object>(() =>
            {
                var element = _elementFinder.FindById(elementId!);
                if (element == null)
                {
                    return new { success = false, error = "Element not found" };
                }

                return _bindingAnalyzer.ForceBindingUpdate(element, propertyName!, direction);
            }));
    }

    private async Task<object> HandleGetLayoutInfoAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _layoutAnalyzer.GetLayoutInfo(elementId)));
    }

    private async Task<object> HandleGetClippingInfoAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _layoutAnalyzer.GetClippingInfo(elementId)));
    }

    private async Task<object> HandleClickElementAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _interactionAnalyzer.ClickElement(elementId)));
    }

    private async Task<object> HandleScrollToElementAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _interactionAnalyzer.ScrollToElement(elementId)));
    }

    private async Task<object> HandleElementScreenshotAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _interactionAnalyzer.TakeScreenshot(elementId)));
    }

    private async Task<object> HandleGetAppliedStylesAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _styleAnalyzer.GetAppliedStyles(elementId)));
    }

    private async Task<object> HandleGetTriggersAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _styleAnalyzer.GetTriggers(elementId)));
    }

    private async Task<object> HandleGetTemplateTreeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _styleAnalyzer.GetTemplateTree(elementId)));
    }

    private async Task<object> HandleCompareTreesAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _visualTreeAnalyzer.CompareTree(elementId)));
    }

    private async Task<object> HandleSerializeToXamlAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke<object>(() =>
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
            }));
    }

    private async Task<object> HandleGetNameScopeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _visualTreeAnalyzer.GetNameScope(elementId)));
    }

    private async Task<object> HandleGetResourceChainAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var resourceKey = GetStringParam(@params, "resourceKey");

        if (string.IsNullOrEmpty(resourceKey))
            throw new ArgumentException("Missing required parameter: resourceKey");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _styleAnalyzer.GetResourceChain(elementId, resourceKey!)));
    }

    private async Task<object> HandleOverrideStyleSetterAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var propertyName = GetStringParam(@params, "propertyName");
        var value = GetObjectParam<object>(@params, "value");

        if (string.IsNullOrEmpty(propertyName))
            throw new ArgumentException("Missing required parameter: propertyName");

        if (value == null)
            throw new ArgumentException("Missing required parameter: value");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _styleAnalyzer.OverrideStyleSetter(elementId, propertyName!, value)));
    }

    private async Task<object> HandleTraceRoutedEventsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var eventName = GetStringParam(@params, "eventName");
        var duration = GetIntParam(@params, "duration") ?? 5000;

        if (string.IsNullOrEmpty(eventName))
            throw new ArgumentException("Missing required parameter: eventName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _eventAnalyzer.TraceRoutedEvents(elementId, eventName!, duration)));
    }

    private async Task<object> HandleFireRoutedEventAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");
        var eventName = GetStringParam(@params, "eventName");
        var eventArgs = GetObjectParam<object>(@params, "eventArgs");

        if (string.IsNullOrEmpty(eventName))
            throw new ArgumentException("Missing required parameter: eventName");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _eventAnalyzer.FireRoutedEvent(elementId, eventName!, eventArgs)));
    }

    private async Task<object> HandleGetRenderStatsAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _performanceAnalyzer.GetRenderStats()));
    }

    private async Task<object> HandleGetVisualCountAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _performanceAnalyzer.GetVisualCount(elementId)));
    }

    private async Task<object> HandleMeasureElementRenderTimeAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        var elementId = GetStringParam(@params, "elementId");

        return await Task.Run(() =>
            Application.Current.Dispatcher.Invoke(() =>
                _performanceAnalyzer.MeasureElementRenderTime(elementId)));
    }

    private async Task<object> HandleNotImplementedAsync(JsonElement? @params, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return new
        {
            success = false,
            message = "This tool is not yet fully implemented in the Inspector. Named Pipe communication is working."
        };
    }

    // Parameter parsing helpers
    private static string? GetStringParam(JsonElement? @params, string name)
    {
        if (@params == null || !@params.HasValue)
            return null;

        if (@params.Value.TryGetProperty(name, out var property))
            return property.GetString();

        return null;
    }

    private static int? GetIntParam(JsonElement? @params, string name)
    {
        if (@params == null || !@params.HasValue)
            return null;

        if (@params.Value.TryGetProperty(name, out var property))
            return property.GetInt32();

        return null;
    }

    private static T? GetObjectParam<T>(JsonElement? @params, string name)
    {
        if (@params == null || !@params.HasValue)
            return default;

        if (@params.Value.TryGetProperty(name, out var property))
            return JsonSerializer.Deserialize<T>(property.GetRawText());

        return default;
    }
}
