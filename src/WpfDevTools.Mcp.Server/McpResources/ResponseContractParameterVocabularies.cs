namespace WpfDevTools.Mcp.Server.McpResources;

internal static class ResponseContractParameterVocabularies
{
    public static object[] GetParameterVocabularies()
    {
        return new object[]
        {
            new
            {
                parameter = "windowFilter",
                tools = new[] { "connect", "get_processes" },
                defaultValue = "visible",
                allowedValues = new[] { "visible", "all", "foreground" }
            },
            new
            {
                parameter = "selectionStrategy",
                tools = new[] { "connect" },
                defaultValue = "single_only",
                allowedValues = new[] { "single_only", "largest_working_set" }
            },
            new
            {
                parameter = "depthMode",
                tools = new[] { "get_ui_summary" },
                defaultValue = "semantic",
                allowedValues = new[] { "semantic", "visual" }
            },
            new
            {
                parameter = "detail",
                tools = new[]
                {
                    "click_element",
                    "execute_command",
                    "modify_viewmodel",
                    "set_dp_value",
                    "clear_dp_value",
                    "fire_routed_event",
                    "override_style_setter"
                },
                defaultValue = "compact",
                allowedValues = new[] { "compact", "minimal", "verbose" },
                compatibilityAliases = new[] { "standard" }
            },
            new
            {
                parameter = "outputMode",
                tools = new[] { "element_screenshot" },
                defaultValue = "metadata",
                allowedValues = new[] { "base64", "metadata", "file" }
            },
            new
            {
                parameter = "screenshotOutputMode",
                tools = new[] { "preview_ui_blueprint" },
                defaultValue = "metadata",
                allowedValues = new[] { "metadata", "file" }
            },
            new
            {
                parameter = "matchMode",
                tools = new[] { "find_elements" },
                defaultValue = "exact",
                allowedValues = new[] { "exact", "contains" }
            },
            new
            {
                parameter = "typeMatchMode",
                tools = new[] { "find_elements" },
                defaultValue = "exact",
                allowedValues = new[] { "exact", "assignable" }
            },
            new
            {
                parameter = "direction",
                tools = new[] { "force_binding_update" },
                defaultValue = "Source",
                allowedValues = new[] { "Source", "Target" }
            },
            new
            {
                parameter = "mode",
                tools = new[] { "trace_routed_events" },
                defaultValue = "capture",
                allowedValues = new[] { "capture", "start", "get" }
            },
            new
            {
                parameter = "statusFilter",
                tools = new[] { "get_bindings" },
                defaultValue = "All",
                allowedValues = new[] { "All", "Active", "Error" }
            },
            new
            {
                parameter = "eventType",
                tools = new[] { "simulate_keyboard" },
                defaultValue = "KeyDown",
                allowedValues = new[] { "KeyDown", "KeyUp" }
            },
            new
            {
                parameter = "eventTypes",
                tools = new[] { "drain_events" },
                defaultValue = "all",
                allowedValues = new[] { "all", "DpChange", "RoutedEvent", "BindingError", "ValidationChange" }
            }
        };
    }
}
