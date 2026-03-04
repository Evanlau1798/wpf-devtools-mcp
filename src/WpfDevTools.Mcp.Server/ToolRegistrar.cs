using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Registers all MCP tools into the tool registry
/// </summary>
public static class ToolRegistrar
{
    /// <summary>
    /// Register all 44 MCP tools across all 10 categories
    /// </summary>
    public static void RegisterAll(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterProcessTools(registry, sessionManager);
        RegisterTreeTools(registry, sessionManager);
        RegisterBindingTools(registry, sessionManager);
        RegisterDependencyPropertyTools(registry, sessionManager);
        RegisterStyleTools(registry, sessionManager);
        RegisterEventTools(registry, sessionManager);
        RegisterInteractionTools(registry, sessionManager);
        RegisterLayoutTools(registry, sessionManager);
        RegisterMvvmTools(registry, sessionManager);
        RegisterPerformanceTools(registry, sessionManager);
    }

    // === 1. Process Management (3 tools) ===
    private static void RegisterProcessTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_processes",
            "[Process] List all running WPF processes available for inspection. Returns: processId, processName, windowTitle, architecture (X86/X64/ARM64), dotNetVersion. Pass processId to connect() to begin inspection.",
            new { type = "object", properties = new { nameFilter = new { type = "string", description = "Filter processes by name (case-insensitive substring match)" } } },
            async (args, ct) => await new GetProcessesTool().ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { },
                new { nameFilter = "TestApp" }
            });

        RegisterTool(registry, "connect",
            "[Process] Connect to a WPF application by injecting the Inspector DLL. MUST be called before any other inspection tool. Returns success status. If already connected, returns immediately.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the target WPF application (from get_processes)" } }, required = new[] { "processId" } },
            async (args, ct) => await new ConnectTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });

        RegisterTool(registry, "ping",
            "[Process] Check connection health and measure round-trip latency to the Inspector DLL in the target process. Returns latency in milliseconds. Use to verify connection is still alive.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" } }, required = new[] { "processId" } },
            async (args, ct) => await new PingTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });
    }

    // === 2. Tree & XAML (6 tools) ===
    private static void RegisterTreeTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_visual_tree",
            "[Tree] Get the Visual Tree (rendering structure) of a WPF element. Returns a hierarchical tree with elementId, type, name, and children for each node. Use elementId from the response in other tools. Use depth=2-4 for large apps to limit response size.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, depth = new { type = "integer", description = "Maximum tree traversal depth (1-100). Use 2-4 for large apps. Default varies by tool." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetVisualTreeTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, depth = 3 },
                new { processId = 12345, elementId = "NameTextBox", depth = 2 }
            });

        RegisterTool(registry, "get_logical_tree",
            "[Tree] Get the Logical Tree (semantic/XAML structure) of a WPF element. Simpler than Visual Tree - shows only elements defined in XAML. Returns elementId, type, name, childCount, and children.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, depth = new { type = "integer", description = "Maximum tree traversal depth (1-100). Use 2-4 for large apps. Default varies by tool." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetLogicalTreeTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, depth = 5 }
            });

        RegisterTool(registry, "serialize_to_xaml",
            "[Tree] Serialize a WPF element to its XAML representation. Returns the XAML markup string for the element and its children. Useful for understanding how elements are structured in markup.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GenericPipeTool(sessionManager, "serialize_to_xaml").ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "get_namescope",
            "[Tree] Get the XAML NameScope of a WPF element. Returns all named elements (x:Name) registered in the element's scope. Useful for discovering elements by name.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GenericPipeTool(sessionManager, "get_namescope").ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });

        RegisterTool(registry, "get_template_tree",
            "[Tree] Get the template Visual Tree of a templated WPF control (Button, ListBox, etc.). Shows the internal rendering structure defined by the control's ControlTemplate. Useful for understanding how a control renders internally.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, depth = new { type = "integer", description = "Maximum tree traversal depth (1-100). Use 2-4 for large apps. Default varies by tool." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetTemplateTreeTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "compare_trees",
            "[Tree] Compare Visual and Logical trees to identify structural differences. Returns elements present in one tree but not the other. Useful for understanding template-generated elements vs XAML-defined elements.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" } }, required = new[] { "processId" } },
            async (args, ct) => await new GenericPipeTool(sessionManager, "compare_trees").ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });
    }

    // === 3. Binding Diagnostics (5 tools) ===
    private static void RegisterBindingTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_bindings",
            "[Binding] Get all DataBindings on an element. Shows binding path, mode (OneWay/TwoWay/OneTime), source type, converter, and current status. Use recursive=true to include child elements.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, recursive = new { type = "boolean", description = "If true, include bindings from all child elements in the subtree. Default: false." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetBindingsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "NameTextBox" },
                new { processId = 12345, recursive = true }
            });

        RegisterTool(registry, "get_binding_errors",
            "[Binding] Get ALL binding errors captured since Inspector connected. FIRST tool to use when debugging data display issues. Returns: elementType, elementName, propertyName, bindingPath, errorType, errorMessage for each error. Empty array means no errors.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" } }, required = new[] { "processId" } },
            async (args, ct) => await new GetBindingErrorsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });

        RegisterTool(registry, "get_binding_value_chain",
            "[Binding] Get the complete value resolution chain for a binding on a specific property. Shows each step from source to target including converters, fallback values, and StringFormat. Useful for diagnosing why a binding produces an unexpected value.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty to inspect the binding chain for (e.g., 'Text', 'Content', 'IsEnabled')" } }, required = new[] { "processId", "propertyName" } },
            async (args, ct) => await new GenericPipeTool(sessionManager, "get_binding_value_chain",
                a =>
                {
                    var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                    if (err != null) return (-1, null, err);
                    var propertyName = ParameterParser.ParseStringParam(a, "propertyName");
                    if (string.IsNullOrEmpty(propertyName)) return (-1, null, (object)new { success = false, error = "Missing required parameter: propertyName" });
                    return (pid, (object?)new { elementId = eid, propertyName }, null);
                }).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text" }
            });

        RegisterTool(registry, "get_datacontext_chain",
            "[Binding] Get the DataContext inheritance chain from an element up to the root. Shows each ancestor's DataContext type and value. Essential for understanding why a binding can't find its source.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetDataContextChainTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "ErrorTextBox1" }
            });

        RegisterTool(registry, "force_binding_update",
            "[Binding] Force a binding to re-evaluate and transfer the current value. Use for UpdateSourceTrigger=Explicit bindings or when the source value changed but the UI didn't update. Triggers both UpdateSource and UpdateTarget.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty whose binding to force-update (e.g., 'Text', 'Content')" } }, required = new[] { "processId", "propertyName" } },
            async (args, ct) => await new GenericPipeTool(sessionManager, "force_binding_update",
                a =>
                {
                    var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                    if (err != null) return (-1, null, err);
                    var propertyName = ParameterParser.ParseStringParam(a, "propertyName");
                    if (string.IsNullOrEmpty(propertyName)) return (-1, null, (object)new { success = false, error = "Missing required parameter: propertyName" });
                    return (pid, (object?)new { elementId = eid, propertyName }, null);
                }).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text" }
            });
    }

    // === 4. DependencyProperty (5 tools) ===
    private static void RegisterDependencyPropertyTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_dp_value_source",
            "[DependencyProperty] Get the value source of a DependencyProperty. Returns where the current value comes from: Default, Inherited, Style, Trigger, TemplateBinding, LocalValue, or Animation. Essential for understanding why a property has a specific value.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty to check (e.g., 'IsEnabled', 'Visibility', 'Text')" } }, required = new[] { "processId", "propertyName" } },
            async (args, ct) => await new GetDpValueSourceTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", propertyName = "IsEnabled" },
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text" }
            });

        RegisterTool(registry, "get_dp_metadata",
            "[DependencyProperty] Get DependencyProperty metadata including default value, inherits flag, affects measure/arrange, and coerce/validation callbacks. Useful for understanding property behavior and framework-level configuration.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty to get metadata for (e.g., 'IsEnabled', 'Visibility')" } }, required = new[] { "processId", "propertyName" } },
            async (args, ct) => await new GetDpMetadataTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, propertyName = "IsEnabled" },
                new { processId = 12345, propertyName = "Visibility" }
            });

        RegisterTool(registry, "set_dp_value",
            "[DependencyProperty] Set a DependencyProperty value at runtime. Value is a string that gets type-converted (e.g., 'True' for bool, 'Red' for Brush, 'Visible'/'Collapsed' for Visibility). Changes are not persisted.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty to set (e.g., 'IsEnabled', 'Visibility', 'Text')" }, value = new { type = "string", description = "String representation of value. Auto-converted to property type. Examples: 'True', '42', 'Red', 'Visible'" } }, required = new[] { "processId", "propertyName", "value" } },
            async (args, ct) => await new SetDpValueTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", propertyName = "IsEnabled", value = "False" },
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text", value = "New Value" }
            });

        RegisterTool(registry, "clear_dp_value",
            "[DependencyProperty] Clear a DependencyProperty local value, reverting it to its inherited, styled, or default value. Useful for removing overrides applied by set_dp_value. Changes are not persisted.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty to clear (e.g., 'IsEnabled', 'Visibility')" } }, required = new[] { "processId", "propertyName" } },
            async (args, ct) => await new ClearDpValueTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", propertyName = "IsEnabled" }
            });

        RegisterTool(registry, "watch_dp_changes",
            "[DependencyProperty] Register a listener for property value changes. NOTE: In STDIO transport, change events are NOT pushed. Use get_dp_value_source to poll for changes. HTTP+SSE transport (planned) will support real-time events.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the DependencyProperty to watch for changes (e.g., 'Text', 'IsEnabled')" } }, required = new[] { "processId", "propertyName" } },
            async (args, ct) => await new WatchDpChangesTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Text" },
                new { processId = 12345, elementId = "SaveButton", propertyName = "IsEnabled" }
            });
    }

    // === 5. Style/Template (4 tools) ===
    private static void RegisterStyleTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_applied_styles",
            "[Style] Get all applied styles on a WPF element. Returns style type, target type, setters (property+value), and whether it's an implicit or explicit style. Use to understand why an element looks a certain way.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetAppliedStylesTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345 }
            });

        RegisterTool(registry, "get_triggers",
            "[Style] Get all triggers from a WPF element's styles and templates. Returns trigger type (Property/Data/Event/MultiTrigger), conditions, and setter actions. Useful for debugging conditional styling.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetTriggersTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "get_resource_chain",
            "[Style] Get the resource lookup chain for a XAML resource key. Shows which ResourceDictionary at which level (element, window, app, theme) provides the resource. Essential for debugging 'resource not found' issues.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, resourceKey = new { type = "string", description = "XAML resource key to look up in the resource chain" } }, required = new[] { "processId", "resourceKey" } },
            async (args, ct) => await new GetResourceChainTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, resourceKey = "PrimaryBrush" },
                new { processId = 12345, elementId = "SaveButton", resourceKey = "ButtonStyle" }
            });

        RegisterTool(registry, "override_style_setter",
            "[Style] Override a style setter value on a WPF element at runtime. Applies a local value that takes precedence over the style. Changes are not persisted. WARNING: modifies the running app.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the property to override (e.g., 'Background', 'Foreground', 'FontSize')" }, value = new { type = "string", description = "String representation of value. Auto-converted to property type. Examples: 'Red', '#FF0000', '14'" } }, required = new[] { "processId", "propertyName", "value" } },
            async (args, ct) => await new OverrideStyleSetterTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", propertyName = "Background", value = "Red" }
            });
    }

    // === 6. RoutedEvent (3 tools) ===
    private static void RegisterEventTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "trace_routed_events",
            "[Event] Start tracing a routed event's propagation path (Tunneling -> Direct -> Bubbling). Returns trace data showing which elements the event passes through. NOTE: Event push requires HTTP+SSE transport (planned).",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, eventName = new { type = "string", description = "WPF RoutedEvent name, e.g., 'ButtonBase.Click', 'UIElement.MouseDown'" } }, required = new[] { "processId", "eventName" } },
            async (args, ct) => await new TraceRoutedEventsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, eventName = "MouseDown" },
                new { processId = 12345, elementId = "SaveButton", eventName = "Click" }
            });

        RegisterTool(registry, "get_event_handlers",
            "[Event] Get all event handlers attached to a WPF element for a specific routed event. Returns handler method names, declaring types, and whether they handle tunneling/bubbling. Use to check why a button click does nothing.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, eventName = new { type = "string", description = "WPF RoutedEvent name, e.g., 'ButtonBase.Click', 'UIElement.MouseDown'" } }, required = new[] { "processId", "eventName" } },
            async (args, ct) => await new GetEventHandlersTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", eventName = "Click" }
            });

        RegisterTool(registry, "fire_routed_event",
            "[Event] Fire a routed event on a WPF element. Triggers the full WPF routed event pipeline (Tunneling -> Direct -> Bubbling). WARNING: This triggers real application logic.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, eventName = new { type = "string", description = "WPF RoutedEvent name, e.g., 'ButtonBase.Click', 'UIElement.MouseDown'" } }, required = new[] { "processId", "eventName" } },
            async (args, ct) => await new FireRoutedEventTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton", eventName = "Click" }
            });
    }

    // === 7. Interaction (5 tools) ===
    private static void RegisterInteractionTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "click_element",
            "[Interaction] Simulate a mouse click on a WPF element. Raises the full WPF click event pipeline. WARNING: This triggers real application logic (e.g., button handlers, navigation).",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new ClickElementTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345, elementId = "ClearButton" }
            });

        RegisterTool(registry, "drag_and_drop",
            "[Interaction] Simulate drag and drop between two WPF elements. Raises DragEnter, DragOver, and Drop events on the target. WARNING: This triggers real application logic.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, sourceElementId = new { type = "string", description = "Element ID of the drag source (obtained from get_visual_tree or get_logical_tree)" }, targetElementId = new { type = "string", description = "Element ID of the drop target (obtained from get_visual_tree or get_logical_tree)" } }, required = new[] { "processId", "sourceElementId", "targetElementId" } },
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
                }).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, sourceElementId = "Item1", targetElementId = "Item2" }
            });

        RegisterTool(registry, "scroll_to_element",
            "[Interaction] Scroll a WPF element into view within its parent ScrollViewer. Calls BringIntoView() on the element. Use before element_screenshot to ensure visibility.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new ScrollToElementTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox" }
            });

        RegisterTool(registry, "simulate_keyboard",
            "[Interaction] Simulate a keyboard key press on an element. Key parameter uses WPF Key enum names: 'Enter', 'Tab', 'Escape', 'Back', 'A'-'Z', 'F1'-'F12', etc. WARNING: This triggers real application logic.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, key = new { type = "string", description = "WPF Key enum name: 'Enter', 'Tab', 'Escape', 'Back', 'A'-'Z', 'F1'-'F12', etc." } }, required = new[] { "processId", "key" } },
            async (args, ct) => await new SimulateKeyboardTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox", key = "Enter" },
                new { processId = 12345, elementId = "NameTextBox", key = "Tab" }
            });

        RegisterTool(registry, "element_screenshot",
            "[Interaction] Capture a PNG screenshot of a specific element. Returns base64-encoded image data. The screenshot is taken on the TARGET MACHINE running the WPF app.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, outputPath = new { type = "string", description = "Optional file path to save screenshot on the target machine. If omitted, returns base64 data." } }, required = new[] { "processId" } },
            async (args, ct) => await new ElementScreenshotTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345 }
            });
    }

    // === 8. Layout (4 tools) ===
    private static void RegisterLayoutTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_layout_info",
            "[Layout] Get layout information of a WPF element. Returns: actualWidth, actualHeight, desiredSize, renderSize, margin, padding, horizontalAlignment, verticalAlignment, position relative to parent and window.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetLayoutInfoTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345 }
            });

        RegisterTool(registry, "get_clipping_info",
            "[Layout] Get clipping information of a WPF element. Returns whether the element is clipped by any ancestor, the clip bounds, and how much content overflows. Useful for debugging elements that appear cut off.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetClippingInfoTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "NameTextBox" }
            });

        RegisterTool(registry, "highlight_element",
            "[Layout] Visually highlight an element with a colored border overlay. Useful for confirming you have the right element. Color accepts WPF color names ('Red', 'Blue', 'Yellow') or hex. Auto-removes after duration.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, color = new { type = "string", description = "WPF color name ('Red','Blue','Yellow') or hex (#AARRGGBB). Default: 'Red'" }, duration = new { type = "integer", description = "Duration in milliseconds before auto-removing the highlight. Default: 2000" } }, required = new[] { "processId" } },
            async (args, ct) => await new GenericPipeTool(sessionManager, "highlight_element",
                a =>
                {
                    var (pid, eid, err) = PipeConnectedToolBase.ParseCommonParams(a);
                    if (err != null) return (-1, null, err);
                    var color = ParameterParser.ParseStringParam(a, "color");
                    var duration = ParameterParser.ParseIntParam(a, "duration");
                    return (pid, (object?)new { elementId = eid, color, duration }, null);
                }).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, elementId = "SaveButton" },
                new { processId = 12345, elementId = "SaveButton", color = "Red", duration = 3000 }
            });

        RegisterTool(registry, "invalidate_layout",
            "[Layout] Force layout invalidation on a WPF element, causing it to re-measure and re-arrange. Use after modifying properties that affect layout to force an immediate update.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new InvalidateLayoutTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "NameTextBox" }
            });
    }

    // === 9. MVVM (5 tools) ===
    private static void RegisterMvvmTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_viewmodel",
            "[MVVM] Get the ViewModel (DataContext) of an element. Returns: typeName, all properties with their current values, and whether INotifyPropertyChanged is implemented.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetViewModelTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "NameTextBox" }
            });

        RegisterTool(registry, "get_commands",
            "[MVVM] Get all ICommand properties from the ViewModel. Returns: commandName, canExecute status, commandType. Use to check why a button is disabled.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetCommandsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "execute_command",
            "[MVVM] Execute an ICommand on the ViewModel. Checks CanExecute first. Returns execution result. WARNING: This triggers real application logic.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, commandName = new { type = "string", description = "Name of the ICommand property on the ViewModel (e.g., 'SaveCommand', 'DeleteCommand')" }, parameter = new { type = "string", description = "Optional command parameter passed to ICommand.Execute(). String value." } }, required = new[] { "processId", "commandName" } },
            async (args, ct) => await new ExecuteCommandTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, commandName = "SaveCommand" },
                new { processId = 12345, elementId = "SaveButton", commandName = "SaveCommand" }
            });

        RegisterTool(registry, "get_validation_errors",
            "[MVVM] Get validation errors from a WPF element. Returns IDataErrorInfo and INotifyDataErrorInfo validation errors, plus Binding.ValidationRules failures. Useful for understanding form validation state.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetValidationErrorsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "AgeTextBox" }
            });

        RegisterTool(registry, "modify_viewmodel",
            "[MVVM] Modify a ViewModel property value via reflection. UI updates automatically ONLY if the ViewModel implements INotifyPropertyChanged. Check get_viewmodel first to confirm property name. WARNING: modifies the running app.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." }, propertyName = new { type = "string", description = "Name of the ViewModel property to modify (e.g., 'Name', 'Age', 'IsActive')" }, value = new { type = "string", description = "New value as a string. Auto-converted to the property type. Examples: 'John Doe', '30', 'true'" } }, required = new[] { "processId", "propertyName", "value" } },
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
                }).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345, propertyName = "Name", value = "John Doe" },
                new { processId = 12345, elementId = "NameTextBox", propertyName = "Age", value = "30" }
            });
    }

    // === 10. Performance (4 tools) ===
    private static void RegisterPerformanceTools(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterTool(registry, "get_render_stats",
            "[Performance] Get render statistics from a WPF application. Returns frame rate, render time, dirty region count, and other WPF rendering pipeline metrics. Use as a first step when investigating slow UI.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" } }, required = new[] { "processId" } },
            async (args, ct) => await new GetRenderStatsTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 }
            });

        RegisterTool(registry, "find_binding_leaks",
            "[Performance] Detect potential binding memory leaks by tracking live binding references. Threshold is the minimum number of live bindings on a single element to flag as suspicious (default: 100).",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, threshold = new { type = "integer", description = "Minimum live binding count per element to flag as a leak candidate. Default: 100" } }, required = new[] { "processId" } },
            async (args, ct) => await new FindBindingLeaksTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, threshold = 50 }
            });

        RegisterTool(registry, "measure_element_render_time",
            "[Performance] Measure the render time of a WPF element in milliseconds. Forces a re-render and measures the time taken. Use to identify slow-rendering elements.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new MeasureElementRenderTimeTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "SaveButton" }
            });

        RegisterTool(registry, "get_visual_count",
            "[Performance] Get the count of visual elements in a WPF element subtree. High counts (>5000) may indicate performance issues. Use to identify overly complex UI subtrees.",
            new { type = "object", properties = new { processId = new { type = "integer", description = "Process ID of the connected WPF application (from get_processes)" }, elementId = new { type = "string", description = "Element ID obtained from get_visual_tree or get_logical_tree. Omit to target root window." } }, required = new[] { "processId" } },
            async (args, ct) => await new GetVisualCountTool(sessionManager).ExecuteAsync(args, ct).ConfigureAwait(false),
            examples: new object[]
            {
                new { processId = 12345 },
                new { processId = 12345, elementId = "NameTextBox" }
            });
    }

    private static void RegisterTool(
        ToolRegistry registry,
        string name,
        string description,
        object schema,
        Func<JsonElement?, CancellationToken, Task<object>> handler,
        object[]? examples = null)
    {
        registry.RegisterTool(new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = schema,
            Examples = examples,
            ExecuteHandler = handler
        });
    }
}
