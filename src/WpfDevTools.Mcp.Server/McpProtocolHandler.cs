using System.Text.Json;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Handles MCP protocol requests (JSON-RPC 2.0)
/// </summary>
public sealed class McpProtocolHandler
{
    private readonly ToolRegistry _toolRegistry;

    // SECURITY: Pre-compiled Regex patterns for error sanitization (avoid per-call allocation)
    private static readonly System.Text.RegularExpressions.Regex FilePathPattern = new(
        @"[A-Za-z]:\\[^\s]+|/[^\s]+",
        System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly System.Text.RegularExpressions.Regex AssemblyNamePattern = new(
        @"\bin\s+[\w\.]+\.dll\b",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

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
                "resources/list" => new { resources = Array.Empty<object>() },
                "prompts/list" => new { prompts = Array.Empty<object>() },
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

    private Task<object> HandleInitializeAsync(JsonElement request, CancellationToken cancellationToken)
    {
        return Task.FromResult<object>(new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { listChanged = false },
                resources = new { listChanged = false },
                prompts = new { listChanged = false }
            },
            serverInfo = new
            {
                name = "wpf-devtools-mcp",
                version = "0.1.0"
            },
            instructions = ServerInstructions
        });
    }

    private const string ServerInstructions = """
        WPF DevTools MCP Server: Deep inspection and interaction with running WPF applications via in-process DLL injection. Provides 44 tools across 10 categories for Visual Tree inspection, Binding diagnostics, MVVM debugging, DependencyProperty analysis, Style/Template inspection, RoutedEvent tracing, element interaction, layout analysis, and performance profiling.

        === MANDATORY WORKFLOW ===
        1. get_processes -> discover running WPF apps and their processIds
        2. connect(processId) -> inject Inspector DLL; MUST succeed before any other tool
        3. Use inspection/interaction tools with the same processId

        === PARAMETER CONVENTIONS ===
        - processId: integer, from get_processes, required by all tools except get_processes
        - elementId: string, from get_visual_tree/get_logical_tree, optional (omit = root window)
        - depth: integer (1-100), controls tree traversal depth, default=10
        - propertyName: string, DependencyProperty name (e.g., 'Text', 'IsEnabled')
        - commandName: string, ICommand property name (e.g., 'SaveCommand')
        - eventName: string, WPF RoutedEvent name (e.g., 'Click', 'MouseDown')
        - resourceKey: string, XAML resource key (e.g., 'PrimaryBrush')

        === TIMEOUTS ===
        - connect(): 30 seconds (DLL injection + IPC handshake)
        - ping(): 5 seconds
        - All other tools: 5 seconds (UI thread operations)
        - If timeout occurs, process may be frozen or unresponsive

        === RATE LIMITS ===
        - Global: 100 requests/minute (returns error -32000 when exceeded)
        - Per-session: 100 requests/minute per connected process
        - Tree tools: Use depth parameter to limit response size
        - Performance tools: Avoid calling in tight loops

        === ELEMENT DISCOVERY ===
        - elementId is required by many tools; omitting it targets the root window
        - First call get_visual_tree or get_logical_tree to discover elementId values
        - Each tree node returns an elementId field - use these in subsequent tool calls
        - elementId format: 'TypeName_N' (e.g., 'Button_1', 'TextBox_5') - stable per session

        === TOOL SELECTION GUIDE ===
        - Blank screen / wrong data? -> get_binding_errors, get_bindings, get_datacontext_chain
        - UI not responding to changes? -> get_dp_value_source, get_viewmodel
        - Button disabled/not working? -> get_commands (CanExecute), get_event_handlers
        - Layout broken? -> get_layout_info (size), get_clipping_info (overflow)
        - Style not applied? -> get_applied_styles, get_resource_chain
        - Performance slow? -> get_visual_count, get_render_stats, find_binding_leaks

        === TOKEN EFFICIENCY ===
        - Use depth=2-3 on tree tools for large apps
        - Use elementId to scope tools to a subtree
        - Use nameFilter on get_processes to reduce response size

        === DESTRUCTIVE TOOLS (modify running app - changes NOT persisted to XAML) ===
        - set_dp_value, clear_dp_value, override_style_setter: change property/style values
        - modify_viewmodel: change ViewModel properties
        - execute_command, fire_routed_event, click_element, simulate_keyboard: trigger actions
        - drag_and_drop: simulate drag-drop operations
        - invalidate_layout: force layout recalculation

        === COMMON WORKFLOWS ===

        Workflow 1 - Debug Binding Error:
        get_processes -> connect -> get_binding_errors -> get_visual_tree(depth=3) -> get_datacontext_chain(elementId) -> get_bindings(elementId)

        Workflow 2 - Test Button Click:
        get_processes -> connect -> get_visual_tree(depth=2) -> get_dp_value_source(elementId, 'IsEnabled') -> click_element(elementId)

        Workflow 3 - Inspect ViewModel:
        get_processes -> connect -> get_viewmodel -> get_commands -> modify_viewmodel(propertyName, value)

        Workflow 4 - Performance Profiling:
        get_processes -> connect -> get_visual_count -> get_render_stats -> find_binding_leaks(threshold=50) -> measure_element_render_time(elementId)

        === ERROR RECOVERY ===
        - "not connected" -> call connect(processId) first, then retry
        - "Access denied" -> restart MCP server as administrator
        - "Not a WPF application" -> use get_processes to find correct processId
        - "Architecture mismatch" -> ensure server and target app match (x64 vs x86)
        - "timeout" -> process may be frozen; try ping() to verify connection
        - "element not found" -> verify elementId from get_visual_tree/get_logical_tree
        - "property not found" -> verify propertyName spelling and element type

        === RESPONSE FORMAT ===
        All tools return JSON: { success: boolean, ...fields }
        On error: { success: false, error: string }

        === LIMITATIONS ===
        - STDIO transport: Cannot push events (watch_dp_changes requires polling)
        - Self-contained single-file apps and Native AOT apps: Cannot inject (use SDK mode)
        - Changes are NOT persisted to XAML files
        """;

    private Task<object> HandleToolsListAsync(CancellationToken cancellationToken)
    {
        var tools = _toolRegistry.GetAllTools()
            .Select(t =>
            {
                var desc = BuildDescriptionWithExamples(t);
                return new
                {
                    name = t.Name,
                    description = desc,
                    inputSchema = t.Parameters
                };
            })
            .ToList();

        return Task.FromResult<object>(new { tools });
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

    private sealed class MethodNotFoundException : Exception
    {
        public MethodNotFoundException(string message) : base(message) { }
    }

    private sealed class InvalidParamsException : Exception
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
        message = FilePathPattern.Replace(message, "[path]");

        // Remove assembly names (e.g., "in WpfDevTools.Mcp.Server.dll")
        message = AssemblyNamePattern.Replace(message, "");

        // Remove stack trace lines and limit to 2 lines for security
        var lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var sanitizedLines = lines
            .Where(line => !line.TrimStart().StartsWith("at "))
            .Take(2); // Limit to first 2 lines

        return string.Join(" ", sanitizedLines);
    }
}
