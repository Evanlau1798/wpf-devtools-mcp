using System.Text.Json;
using System.Windows;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Inspector.Analyzers;

namespace WpfDevTools.Inspector.Host;

/// <summary>
/// Dispatches incoming requests to appropriate handlers
/// </summary>
public class RequestDispatcher
{
    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object>>> _handlers;
    private readonly VisualTreeAnalyzer _visualTreeAnalyzer;
    private readonly BindingAnalyzer _bindingAnalyzer;

    public RequestDispatcher()
    {
        // Initialize analyzers
        _visualTreeAnalyzer = new VisualTreeAnalyzer();
        _bindingAnalyzer = new BindingAnalyzer();

        _handlers = new Dictionary<string, Func<JsonElement?, CancellationToken, Task<object>>>
        {
            // Implemented tools
            ["ping"] = HandlePingAsync,
            ["get_visual_tree"] = HandleGetVisualTreeAsync,
            ["get_bindings"] = HandleGetBindingsAsync,
            ["get_binding_errors"] = HandleGetBindingErrorsAsync,
            ["get_datacontext_chain"] = HandleGetDataContextChainAsync,

            // Placeholder tools (to be implemented)
            ["get_logical_tree"] = HandleNotImplementedAsync,
            ["compare_trees"] = HandleNotImplementedAsync,
            ["get_viewmodel"] = HandleNotImplementedAsync,
            ["get_commands"] = HandleNotImplementedAsync,
            ["execute_command"] = HandleNotImplementedAsync,
            ["get_validation_errors"] = HandleNotImplementedAsync,
            ["get_dp_value_source"] = HandleNotImplementedAsync,
            ["set_dp_value"] = HandleNotImplementedAsync,
            ["clear_dp_value"] = HandleNotImplementedAsync,
            ["get_layout_info"] = HandleNotImplementedAsync,
            ["get_clipping_info"] = HandleNotImplementedAsync,
            ["click_element"] = HandleNotImplementedAsync,
            ["scroll_to_element"] = HandleNotImplementedAsync,
            ["element_screenshot"] = HandleNotImplementedAsync,
            ["get_applied_styles"] = HandleNotImplementedAsync,
            ["get_triggers"] = HandleNotImplementedAsync,
            ["get_template_tree"] = HandleNotImplementedAsync,
            ["trace_routed_events"] = HandleNotImplementedAsync,
            ["fire_routed_event"] = HandleNotImplementedAsync,
            ["get_render_stats"] = HandleNotImplementedAsync,
            ["get_visual_count"] = HandleNotImplementedAsync,
            ["measure_element_render_time"] = HandleNotImplementedAsync,

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
}
