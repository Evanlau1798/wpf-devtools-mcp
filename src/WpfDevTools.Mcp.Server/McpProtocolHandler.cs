using System.Text.Json;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Handles MCP protocol requests (JSON-RPC 2.0)
/// </summary>
public class McpProtocolHandler
{
    private readonly ToolRegistry _toolRegistry;

    /// <summary>
    /// Initializes a new instance of the McpProtocolHandler class
    /// </summary>
    /// <param name="toolRegistry">Optional tool registry for custom tool registration</param>
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
            instructions = "WPF DevTools MCP Server: Deep inspection and interaction with running WPF applications via in-process DLL injection.\n\n"
                + "=== MANDATORY WORKFLOW ===\n"
                + "1. get_processes → discover running WPF apps and their processIds\n"
                + "2. connect(processId) → inject Inspector DLL; MUST succeed before any other tool\n"
                + "3. Use inspection/interaction tools with the same processId\n\n"
                + "=== PARAMETER NAMING CONVENTIONS ===\n"
                + "- processId: integer, from get_processes, required by all tools except get_processes\n"
                + "- elementId: string, from get_visual_tree/get_logical_tree, optional (omit = root window)\n"
                + "- depth: integer (1-100), controls tree traversal depth, default=10\n"
                + "- propertyName: string, DependencyProperty name (e.g., 'Text', 'IsEnabled')\n"
                + "- commandName: string, ICommand property name (e.g., 'SaveCommand')\n"
                + "- eventName: string, WPF RoutedEvent name (e.g., 'Click', 'MouseDown')\n"
                + "- resourceKey: string, XAML resource key (e.g., 'PrimaryBrush')\n\n"
                + "=== TIMEOUTS ===\n"
                + "- connect(): 30 seconds (DLL injection + IPC handshake)\n"
                + "- ping(): 5 seconds\n"
                + "- All other tools: 5 seconds (UI thread operations)\n"
                + "- If timeout occurs, process may be frozen or unresponsive\n\n"
                + "=== RATE LIMITS ===\n"
                + "- No hard rate limits enforced\n"
                + "- Recommended: <10 requests/second to avoid overwhelming target app\n"
                + "- Tree tools (get_visual_tree, get_logical_tree): Use depth parameter to limit response size\n"
                + "- Performance tools: Avoid calling in tight loops (causes measurement overhead)\n\n"
                + "=== ELEMENT DISCOVERY ===\n"
                + "- elementId is required by many tools; omitting it targets the root window\n"
                + "- To inspect a specific element, first call get_visual_tree or get_logical_tree to discover element IDs\n"
                + "- Use the returned elementId values in subsequent tool calls\n"
                + "- elementId format: element name (x:Name) or auto-generated ID\n\n"
                + "=== TOOL SELECTION GUIDE ===\n"
                + "- Blank screen / wrong data? → get_binding_errors, then get_bindings, then get_datacontext_chain\n"
                + "- UI not responding to changes? → get_dp_value_source to check binding; get_viewmodel to inspect VM\n"
                + "- Button disabled/not working? → get_commands to check CanExecute; get_event_handlers for Click\n"
                + "- Layout broken? → get_layout_info for size; get_clipping_info for overflow\n"
                + "- Style not applied? → get_applied_styles; get_resource_chain to trace lookup\n"
                + "- Performance slow? → get_visual_count for tree size; get_render_stats; find_binding_leaks\n\n"
                + "=== TOKEN EFFICIENCY ===\n"
                + "- Use depth=2 or depth=3 on tree tools for large apps\n"
                + "- Use elementId to scope tools to a subtree\n"
                + "- Use nameFilter on get_processes to reduce response size\n"
                + "- Tree tools return hierarchical data; parse incrementally\n\n"
                + "=== DESTRUCTIVE TOOLS (modify the running app - changes are NOT persisted) ===\n"
                + "- set_dp_value, clear_dp_value, override_style_setter: change property/style values\n"
                + "- modify_viewmodel: change ViewModel properties\n"
                + "- execute_command, fire_routed_event, click_element, simulate_keyboard: trigger actions\n"
                + "- drag_and_drop: simulate drag-drop operations\n"
                + "- invalidate_layout: force layout recalculation\n\n"
                + "=== COMMON WORKFLOWS ===\n\n"
                + "Workflow 1: Debug Binding Error\n"
                + "1. get_processes → find target app processId\n"
                + "2. connect(processId) → inject inspector\n"
                + "3. get_binding_errors(processId) → identify errors\n"
                + "4. get_visual_tree(processId, depth=3) → find element with error\n"
                + "5. get_datacontext_chain(processId, elementId) → trace DataContext\n"
                + "6. get_bindings(processId, elementId) → inspect binding configuration\n\n"
                + "Workflow 2: Test Button Click\n"
                + "1. get_processes → find target app\n"
                + "2. connect(processId)\n"
                + "3. get_visual_tree(processId, depth=2) → find button elementId\n"
                + "4. get_dp_value_source(processId, elementId, 'IsEnabled') → verify enabled\n"
                + "5. get_event_handlers(processId, elementId, 'Click') → verify handler attached\n"
                + "6. click_element(processId, elementId) → simulate click\n\n"
                + "Workflow 3: Inspect ViewModel State\n"
                + "1. get_processes → find target app\n"
                + "2. connect(processId)\n"
                + "3. get_viewmodel(processId) → inspect root ViewModel\n"
                + "4. get_commands(processId) → check command CanExecute status\n"
                + "5. modify_viewmodel(processId, 'PropertyName', 'NewValue') → test UI updates\n\n"
                + "Workflow 4: Performance Profiling\n"
                + "1. get_processes → find target app\n"
                + "2. connect(processId)\n"
                + "3. get_visual_count(processId) → check total element count\n"
                + "4. get_render_stats(processId) → measure frame rate\n"
                + "5. find_binding_leaks(processId, threshold=50) → detect leak candidates\n"
                + "6. get_visual_tree(processId, depth=2) → identify complex subtrees\n"
                + "7. measure_element_render_time(processId, elementId) → profile specific elements\n\n"
                + "=== ERROR RECOVERY ===\n"
                + "- \"not connected\" → call connect(processId) first, then retry\n"
                + "- \"Access denied\" → restart MCP server as administrator\n"
                + "- \"Not a WPF application\" → use get_processes to find correct processId\n"
                + "- \"Architecture mismatch\" → ensure server and target app match (x64 vs x86)\n"
                + "- \"timeout\" → process may be frozen; try ping() to verify connection\n"
                + "- \"element not found\" → verify elementId from get_visual_tree/get_logical_tree\n"
                + "- \"property not found\" → verify propertyName spelling and element type\n\n"
                + "=== RESPONSE FORMAT ===\n"
                + "All tools return JSON with:\n"
                + "- success: boolean (true = operation succeeded)\n"
                + "- Additional fields vary by tool (see tool descriptions)\n"
                + "- On error: success=false, error: string with error message\n\n"
                + "=== LIMITATIONS ===\n"
                + "- STDIO transport: Cannot push events (watch_dp_changes, trace_routed_events require polling)\n"
                + "- HTTP+SSE transport (planned Phase 2+): Enables real-time event push\n"
                + "- Self-contained single-file apps: Cannot inject (use opt-in SDK mode)\n"
                + "- Native AOT apps: Cannot inject\n"
                + "- Changes are NOT persisted to XAML files"
        };
    }

    private async Task<object> HandleToolsListAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Suppress async warning

        var tools = _toolRegistry.GetAllTools()
            .Select(t => new
            {
                name = t.Name,
                description = BuildDescriptionWithExamples(t),
                inputSchema = t.Parameters
            })
            .ToList();

        return new { tools };
    }

    private static string BuildDescriptionWithExamples(ToolDefinition tool)
    {
        if (tool.Examples == null || tool.Examples.Length == 0)
            return tool.Description;

        var sb = new System.Text.StringBuilder(tool.Description);
        sb.Append(" Example: ");
        sb.Append(string.Join(" | ", tool.Examples.Select(e => JsonSerializer.Serialize(e))));
        return sb.ToString();
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
            var element = JsonSerializer.SerializeToElement(result);
            return element.TryGetProperty("success", out var successProp) &&
                   successProp.ValueKind == JsonValueKind.False;
        }
        catch
        {
            // If we can't determine, assume not an error
            return false;
        }
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
    /// Sanitize error messages to prevent leaking internal file paths, stack traces, and assembly names
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

        // Remove assembly names (e.g., "in WpfDevTools.Mcp.Server.dll")
        message = System.Text.RegularExpressions.Regex.Replace(
            message,
            @"\bin\s+[\w\.]+\.dll\b",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove stack trace lines and limit to 2 lines for security
        var lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var sanitizedLines = lines
            .Where(line => !line.TrimStart().StartsWith("at "))
            .Take(2); // Limit to first 2 lines

        return string.Join(" ", sanitizedLines);
    }
}
