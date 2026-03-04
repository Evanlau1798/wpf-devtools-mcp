using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;

// Initialize components
var logger = new FileLogger();
var toolRegistry = new ToolRegistry();
var protocolHandler = new McpProtocolHandler(toolRegistry);

logger.LogInfo("WPF DevTools MCP Server starting...");
logger.LogInfo($"Log file: {logger.LogFilePath}");

// Register core tools
RegisterCoreTools(toolRegistry, logger);

logger.LogInfo("MCP Server ready. Listening on STDIN...");

// STDIO transport loop
try
{
    using var stdin = Console.OpenStandardInput();
    using var stdout = Console.OpenStandardOutput();
    using var reader = new StreamReader(stdin);
    using var writer = new StreamWriter(stdout) { AutoFlush = true };

    while (!reader.EndOfStream)
    {
        var line = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(line))
            continue;

        logger.LogDebug($"Received: {line}");

        try
        {
            var response = await protocolHandler.HandleRequestAsync(line, CancellationToken.None);
            await writer.WriteLineAsync(response);
            logger.LogDebug($"Sent: {response}");
        }
        catch (Exception ex)
        {
            logger.LogError($"Error processing request: {ex.Message}");
        }
    }
}
catch (Exception ex)
{
    logger.LogError($"Fatal error: {ex}");
    return 1;
}

logger.LogInfo("MCP Server shutting down.");
return 0;

static void RegisterCoreTools(ToolRegistry registry, FileLogger logger)
{
    logger.LogInfo("Registering core tools...");

    var sessionManager = new SessionManager();

    // === 1. Process Management (3 tools) ===
    RegisterTool(registry, "get_processes",
        "List all running WPF processes that can be inspected",
        new { type = "object", properties = new { nameFilter = new { type = "string", description = "Filter processes by name" } } },
        async (args, ct) => await new GetProcessesTool().ExecuteAsync(args, ct));

    RegisterTool(registry, "connect",
        "Connect to a WPF process by injecting the Inspector DLL",
        new { type = "object", properties = new { processId = new { type = "integer", description = "Target process ID" } }, required = new[] { "processId" } },
        async (args, ct) => await new ConnectTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "ping",
        "Ping a connected WPF process to check if it is still responsive",
        new { type = "object", properties = new { processId = new { type = "integer", description = "Target process ID" } }, required = new[] { "processId" } },
        async (args, ct) => await new PingTool(sessionManager).ExecuteAsync(args, ct));

    // === 2. Tree & XAML (5 tools) ===
    RegisterTool(registry, "get_visual_tree",
        "Get the Visual Tree of a WPF application",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string", description = "Root element ID (optional)" }, depth = new { type = "integer", description = "Max depth to traverse" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetVisualTreeTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "get_logical_tree",
        "Get the Logical Tree of a WPF application",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string", description = "Root element ID (optional)" }, depth = new { type = "integer", description = "Max depth to traverse" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetLogicalTreeTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "compare_trees",
        "Compare Visual Tree and Logical Tree of a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string", description = "Element ID to compare trees from" } }, required = new[] { "processId" } },
        async (args, ct) => await new GenericPipeTool(sessionManager, "compare_trees").ExecuteAsync(args, ct));

    RegisterTool(registry, "serialize_to_xaml",
        "Serialize a WPF element subtree to XAML markup",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string", description = "Element ID to serialize" } }, required = new[] { "processId" } },
        async (args, ct) => await new GenericPipeTool(sessionManager, "serialize_to_xaml").ExecuteAsync(args, ct));

    RegisterTool(registry, "get_namescope",
        "Get the NameScope of a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string", description = "Element ID to get NameScope from" } }, required = new[] { "processId" } },
        async (args, ct) => await new GenericPipeTool(sessionManager, "get_namescope").ExecuteAsync(args, ct));

    RegisterTool(registry, "get_template_tree",
        "Get the template tree of a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, maxDepth = new { type = "integer", description = "Max depth to traverse" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetTemplateTreeTool(sessionManager).ExecuteAsync(args, ct));

    // === 3. Binding Diagnostics (5 tools) ===
    RegisterTool(registry, "get_bindings",
        "Get all data bindings on a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetBindingsTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "get_binding_errors",
        "Get all binding errors in a WPF application",
        new { type = "object", properties = new { processId = new { type = "integer" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetBindingErrorsTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "get_datacontext_chain",
        "Get the DataContext inheritance chain for a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetDataContextChainTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "get_binding_value_chain",
        "Get the binding value resolution chain from source to target",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, propertyName = new { type = "string", description = "DependencyProperty name to inspect" } }, required = new[] { "processId", "propertyName" } },
        async (args, ct) => await new GenericPipeTool(sessionManager, "get_binding_value_chain",
            a =>
            {
                var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                if (err != null) return (-1, null, err);
                var propertyName = ParameterParser.ParseStringParam(a, "propertyName");
                if (string.IsNullOrEmpty(propertyName)) return (-1, null, (object)new { success = false, error = "Missing required parameter: propertyName" });
                return (pid, (object?)new { elementId = eid, propertyName }, null);
            }).ExecuteAsync(args, ct));

    RegisterTool(registry, "force_binding_update",
        "Force a binding to update its source or target",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, propertyName = new { type = "string", description = "DependencyProperty name" }, direction = new { type = "string", description = "Update direction: 'Source' or 'Target'" } }, required = new[] { "processId", "propertyName", "direction" } },
        async (args, ct) => await new GenericPipeTool(sessionManager, "force_binding_update",
            a =>
            {
                var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                if (err != null) return (-1, null, err);
                var propertyName = ParameterParser.ParseStringParam(a, "propertyName");
                var direction = ParameterParser.ParseStringParam(a, "direction");
                if (string.IsNullOrEmpty(propertyName)) return (-1, null, (object)new { success = false, error = "Missing required parameter: propertyName" });
                if (string.IsNullOrEmpty(direction)) return (-1, null, (object)new { success = false, error = "Missing required parameter: direction" });
                return (pid, (object?)new { elementId = eid, propertyName, direction }, null);
            }).ExecuteAsync(args, ct));

    // === 4. DependencyProperty (5 tools) ===
    RegisterTool(registry, "get_dp_value_source",
        "Get the value source of a DependencyProperty (local, style, trigger, default, etc.)",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, propertyName = new { type = "string" } }, required = new[] { "processId", "propertyName" } },
        async (args, ct) => await new GetDpValueSourceTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "get_dp_metadata",
        "Get DependencyProperty metadata including default value and flags",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, propertyName = new { type = "string" } }, required = new[] { "processId", "propertyName" } },
        async (args, ct) => await new GetDpMetadataTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "set_dp_value",
        "Set a DependencyProperty value on a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, propertyName = new { type = "string" }, value = new { type = "string" } }, required = new[] { "processId", "propertyName", "value" } },
        async (args, ct) => await new SetDpValueTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "clear_dp_value",
        "Clear a DependencyProperty local value on a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, propertyName = new { type = "string" } }, required = new[] { "processId", "propertyName" } },
        async (args, ct) => await new ClearDpValueTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "watch_dp_changes",
        "Watch for DependencyProperty value changes on a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, propertyName = new { type = "string" } }, required = new[] { "processId", "propertyName" } },
        async (args, ct) => await new WatchDpChangesTool(sessionManager).ExecuteAsync(args, ct));

    // === 5. Style/Template (5 tools) ===
    RegisterTool(registry, "get_applied_styles",
        "Get all applied styles on a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetAppliedStylesTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "get_triggers",
        "Get triggers from WPF element styles and templates",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetTriggersTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "get_resource_chain",
        "Get the resource lookup chain for a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, resourceKey = new { type = "string", description = "Resource key to look up" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetResourceChainTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "override_style_setter",
        "Override a style setter value on a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, propertyName = new { type = "string" }, value = new { type = "string" } }, required = new[] { "processId", "propertyName", "value" } },
        async (args, ct) => await new OverrideStyleSetterTool(sessionManager).ExecuteAsync(args, ct));

    // === 6. RoutedEvent (3 tools) ===
    RegisterTool(registry, "trace_routed_events",
        "Trace routed events passing through a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, eventName = new { type = "string", description = "Name of the routed event to trace" } }, required = new[] { "processId", "eventName" } },
        async (args, ct) => await new TraceRoutedEventsTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "get_event_handlers",
        "Get all event handlers attached to a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetEventHandlersTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "fire_routed_event",
        "Fire a routed event on a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, eventName = new { type = "string", description = "Name of the routed event to fire" } }, required = new[] { "processId", "eventName" } },
        async (args, ct) => await new FireRoutedEventTool(sessionManager).ExecuteAsync(args, ct));

    // === 7. Interaction (5 tools) ===
    RegisterTool(registry, "click_element",
        "Simulate a mouse click on a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new ClickElementTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "drag_and_drop",
        "Simulate drag and drop between two WPF elements",
        new { type = "object", properties = new { processId = new { type = "integer" }, sourceElementId = new { type = "string", description = "Source element ID" }, targetElementId = new { type = "string", description = "Target element ID" } }, required = new[] { "processId", "sourceElementId", "targetElementId" } },
        async (args, ct) => await new GenericPipeTool(sessionManager, "drag_and_drop",
            a =>
            {
                var (pid, _, err) = PipeConnectedToolBase.ParseCommonParams(a);
                if (err != null) return (-1, null, err);
                var sourceElementId = ParameterParser.ParseStringParam(a, "sourceElementId");
                var targetElementId = ParameterParser.ParseStringParam(a, "targetElementId");
                if (string.IsNullOrEmpty(sourceElementId)) return (-1, null, (object)new { success = false, error = "Missing required parameter: sourceElementId" });
                if (string.IsNullOrEmpty(targetElementId)) return (-1, null, (object)new { success = false, error = "Missing required parameter: targetElementId" });
                return (pid, (object?)new { sourceElementId, targetElementId }, null);
            }).ExecuteAsync(args, ct));

    RegisterTool(registry, "scroll_to_element",
        "Scroll a WPF element into view",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new ScrollToElementTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "simulate_keyboard",
        "Simulate keyboard input on a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, key = new { type = "string", description = "Key to simulate (e.g., Enter, Tab, A)" } }, required = new[] { "processId", "key" } },
        async (args, ct) => await new SimulateKeyboardTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "element_screenshot",
        "Capture a screenshot of a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, outputPath = new { type = "string", description = "File path to save screenshot" } }, required = new[] { "processId" } },
        async (args, ct) => await new ElementScreenshotTool(sessionManager).ExecuteAsync(args, ct));

    // === 8. Layout (4 tools) ===
    RegisterTool(registry, "get_layout_info",
        "Get layout information (position, size, margin, padding) of a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetLayoutInfoTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "get_clipping_info",
        "Get clipping information of a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetClippingInfoTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "highlight_element",
        "Highlight a WPF element with a visual overlay",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, color = new { type = "string", description = "Highlight color (optional)" }, duration = new { type = "integer", description = "Duration in milliseconds (optional)" } }, required = new[] { "processId" } },
        async (args, ct) => await new GenericPipeTool(sessionManager, "highlight_element",
            a =>
            {
                var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                if (err != null) return (-1, null, err);
                var color = ParameterParser.ParseStringParam(a, "color");
                var duration = ParameterParser.ParseIntParam(a, "duration");
                return (pid, (object?)new { elementId = eid, color, duration }, null);
            }).ExecuteAsync(args, ct));

    RegisterTool(registry, "invalidate_layout",
        "Force layout invalidation on a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new InvalidateLayoutTool(sessionManager).ExecuteAsync(args, ct));

    // === 9. MVVM (5 tools) ===
    RegisterTool(registry, "get_viewmodel",
        "Get the ViewModel (DataContext) of a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetViewModelTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "get_commands",
        "Get all ICommand properties from a WPF element's DataContext",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetCommandsTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "execute_command",
        "Execute an ICommand on a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, commandName = new { type = "string", description = "Name of the command to execute" }, parameter = new { type = "string", description = "Optional command parameter" } }, required = new[] { "processId", "commandName" } },
        async (args, ct) => await new ExecuteCommandTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "get_validation_errors",
        "Get validation errors from a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetValidationErrorsTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "modify_viewmodel",
        "Modify a ViewModel property value at runtime",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" }, propertyName = new { type = "string", description = "Property name to modify" }, value = new { type = "string", description = "New value to set" } }, required = new[] { "processId", "propertyName", "value" } },
        async (args, ct) => await new GenericPipeTool(sessionManager, "modify_viewmodel",
            a =>
            {
                var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                if (err != null) return (-1, null, err);
                var propertyName = ParameterParser.ParseStringParam(a, "propertyName");
                var value = ParameterParser.ParseStringParam(a, "value");
                if (string.IsNullOrEmpty(propertyName)) return (-1, null, (object)new { success = false, error = "Missing required parameter: propertyName" });
                if (string.IsNullOrEmpty(value)) return (-1, null, (object)new { success = false, error = "Missing required parameter: value" });
                return (pid, (object?)new { elementId = eid, propertyName, value }, null);
            }).ExecuteAsync(args, ct));

    // === 10. Performance (4 tools) ===
    RegisterTool(registry, "get_render_stats",
        "Get render statistics from a WPF application",
        new { type = "object", properties = new { processId = new { type = "integer" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetRenderStatsTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "find_binding_leaks",
        "Find potential binding memory leaks in a WPF application",
        new { type = "object", properties = new { processId = new { type = "integer" }, threshold = new { type = "integer", description = "Leak detection threshold (default: 100)" } }, required = new[] { "processId" } },
        async (args, ct) => await new FindBindingLeaksTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "measure_element_render_time",
        "Measure the render time of a WPF element",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new MeasureElementRenderTimeTool(sessionManager).ExecuteAsync(args, ct));

    RegisterTool(registry, "get_visual_count",
        "Get the count of visual elements in a WPF element subtree",
        new { type = "object", properties = new { processId = new { type = "integer" }, elementId = new { type = "string" } }, required = new[] { "processId" } },
        async (args, ct) => await new GetVisualCountTool(sessionManager).ExecuteAsync(args, ct));

    logger.LogInfo($"Registered {registry.GetAllTools().Count} tools");
}

static void RegisterTool(
    ToolRegistry registry,
    string name,
    string description,
    object schema,
    Func<JsonElement?, CancellationToken, Task<object>> handler)
{
    registry.RegisterTool(new ToolDefinition
    {
        Name = name,
        Description = description,
        Parameters = schema,
        ExecuteHandler = handler
    });
}
