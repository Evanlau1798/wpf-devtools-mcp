using System.Text.Json;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Handles MCP protocol requests (JSON-RPC 2.0)
/// </summary>
public class McpProtocolHandler
{
    private readonly ToolRegistry _toolRegistry;

    public McpProtocolHandler(ToolRegistry? toolRegistry = null)
    {
        _toolRegistry = toolRegistry ?? new ToolRegistry();
    }

    /// <summary>
    /// Handle incoming JSON-RPC request.
    /// Returns null for notifications (no "id" field) that should not receive a response.
    /// </summary>
    public async Task<string?> HandleRequestAsync(string requestJson, CancellationToken cancellationToken)
    {
        JsonElement? id = null;

        try
        {
            // Parse request
            var request = JsonSerializer.Deserialize<JsonElement>(requestJson);

            id = request.TryGetProperty("id", out var idProp) ? idProp : (JsonElement?)null;
            var method = request.TryGetProperty("method", out var methodProp)
                ? methodProp.GetString()
                : null;

            if (string.IsNullOrEmpty(method))
            {
                return CreateErrorResponse(id, -32600, "Invalid Request: missing method");
            }

            // Handle notifications (no "id" field) - do not send a response
            if (!id.HasValue)
            {
                return method switch
                {
                    "notifications/initialized" => null,
                    _ => null // Unknown notifications are silently ignored per MCP spec
                };
            }

            // Route to handler
            object? result = method switch
            {
                "initialize" => await HandleInitializeAsync(request, cancellationToken).ConfigureAwait(false),
                "tools/list" => await HandleToolsListAsync(cancellationToken).ConfigureAwait(false),
                "tools/call" => await HandleToolsCallAsync(request, cancellationToken).ConfigureAwait(false),
                _ => throw new MethodNotFoundException($"Method not found: {method}")
            };

            return CreateSuccessResponse(id, result);
        }
        catch (JsonException)
        {
            return CreateErrorResponse(id, -32700, "Parse error");
        }
        catch (MethodNotFoundException ex)
        {
            return CreateErrorResponse(id, -32601, ex.Message);
        }
        catch (InvalidParamsException ex)
        {
            return CreateErrorResponse(id, -32602, ex.Message);
        }
        catch (Exception ex)
        {
            // Sanitize error message to avoid leaking internal paths
            var sanitizedMessage = SanitizeErrorMessage(ex.Message);
            return CreateErrorResponse(id, -32603, $"Internal error: {sanitizedMessage}");
        }
    }

    private async Task<object> HandleInitializeAsync(JsonElement request, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Suppress async warning

        return new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { tools = new { } },
            serverInfo = new
            {
                name = "wpf-devtools-mcp",
                version = "0.1.0"
            },
            instructions = "WPF DevTools MCP Server enables deep inspection and interaction with running WPF applications via in-process DLL injection. "
                + "Workflow: 1) Call get_processes to list running WPF apps. 2) Call connect with a processId to inject the Inspector DLL. 3) Use inspection tools. "
                + "All tools except get_processes require a prior connect call for the target processId. "
                + "Tool categories: Process Management (get_processes, connect, ping), "
                + "Tree & XAML (get_visual_tree, get_logical_tree, compare_trees, serialize_to_xaml, get_namescope, get_template_tree), "
                + "Binding Diagnostics (get_bindings, get_binding_errors, get_datacontext_chain, get_binding_value_chain, force_binding_update), "
                + "DependencyProperty (get_dp_value_source, get_dp_metadata, set_dp_value, clear_dp_value, watch_dp_changes), "
                + "Style/Template (get_applied_styles, get_triggers, get_resource_chain, override_style_setter), "
                + "RoutedEvent (trace_routed_events, get_event_handlers, fire_routed_event), "
                + "Interaction (click_element, drag_and_drop, scroll_to_element, simulate_keyboard, element_screenshot), "
                + "Layout (get_layout_info, get_clipping_info, highlight_element, invalidate_layout), "
                + "MVVM (get_viewmodel, get_commands, execute_command, get_validation_errors, modify_viewmodel), "
                + "Performance (get_render_stats, find_binding_leaks, measure_element_render_time, get_visual_count). "
                + "Parameters: elementId defaults to root element if omitted; depth limits tree traversal depth."
        };
    }

    private async Task<object> HandleToolsListAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Suppress async warning

        var tools = _toolRegistry.GetAllTools()
            .Select(t => new
            {
                name = t.Name,
                description = t.Description,
                inputSchema = t.Parameters
            })
            .ToList();

        return new { tools };
    }

    private async Task<object> HandleToolsCallAsync(JsonElement request, CancellationToken cancellationToken)
    {
        if (!request.TryGetProperty("params", out var paramsElement))
        {
            throw new InvalidParamsException("Missing params in tools/call request");
        }

        if (!paramsElement.TryGetProperty("name", out var nameProp))
        {
            throw new InvalidParamsException("Missing required parameter: name");
        }

        var toolName = nameProp.GetString();
        if (string.IsNullOrEmpty(toolName))
        {
            throw new InvalidParamsException("Parameter 'name' must be a non-empty string");
        }

        var tool = _toolRegistry.GetTool(toolName)
            ?? throw new MethodNotFoundException($"Tool not found: {toolName}");

        if (tool.ExecuteHandler == null)
            throw new InvalidOperationException($"Tool '{toolName}' has no execute handler");

        var arguments = paramsElement.TryGetProperty("arguments", out var args) ? args : (JsonElement?)null;

        var result = await tool.ExecuteHandler(arguments, cancellationToken).ConfigureAwait(false);

        // Check if the tool result indicates an error (success: false)
        var isError = IsToolResultError(result);

        if (isError)
        {
            return new
            {
                content = new[]
                {
                    new { type = "text", text = JsonSerializer.Serialize(result) }
                },
                isError = true
            };
        }

        return new
        {
            content = new[]
            {
                new { type = "text", text = JsonSerializer.Serialize(result) }
            }
        };
    }

    /// <summary>
    /// Check if a tool result represents an error (has success=false)
    /// </summary>
    private static bool IsToolResultError(object result)
    {
        try
        {
            var json = JsonSerializer.Serialize(result);
            var element = JsonSerializer.Deserialize<JsonElement>(json);

            if (element.TryGetProperty("success", out var successProp) &&
                successProp.ValueKind == JsonValueKind.False)
            {
                return true;
            }
        }
        catch
        {
            // If we can't determine, assume not an error
        }

        return false;
    }

    private string CreateSuccessResponse(JsonElement? id, object? result)
    {
        var response = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ExtractIdValue(id),
            ["result"] = result
        };

        return JsonSerializer.Serialize(response);
    }

    private string CreateErrorResponse(JsonElement? id, int code, string message)
    {
        var response = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ExtractIdValue(id),
            ["error"] = new
            {
                code,
                message
            }
        };

        return JsonSerializer.Serialize(response);
    }

    private static object? ExtractIdValue(JsonElement? id)
    {
        if (!id.HasValue)
            return null;

        return id.Value.ValueKind switch
        {
            JsonValueKind.Number => (object)id.Value.GetInt32(),
            JsonValueKind.String => (object)id.Value.GetString()!,
            _ => null
        };
    }

    private class MethodNotFoundException : Exception
    {
        public MethodNotFoundException(string message) : base(message) { }
    }

    private class InvalidParamsException : Exception
    {
        public InvalidParamsException(string message) : base(message) { }
    }

    /// <summary>
    /// Sanitize error messages to prevent leaking internal file paths and stack traces
    /// </summary>
    private static string SanitizeErrorMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return "An error occurred";

        // Remove file paths (Windows and Unix style)
        message = System.Text.RegularExpressions.Regex.Replace(
            message,
            @"[A-Za-z]:\\[^\s]+|/[^\s]+",
            "[path]");

        // Remove stack trace lines
        var lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var sanitizedLines = lines
            .Where(line => !line.TrimStart().StartsWith("at "))
            .Take(3); // Limit to first 3 lines

        return string.Join(" ", sanitizedLines);
    }
}
