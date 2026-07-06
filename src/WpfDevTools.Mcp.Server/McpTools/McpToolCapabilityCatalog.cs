using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

internal static class McpToolCapabilityTags
{
    internal const string Destructive = "destructive";
    internal const string SensitiveRead = "sensitive-read";
    internal const string Screenshot = "screenshot";
    internal const string ViewModel = "viewmodel";
}

internal static class McpToolPolicyTags
{
    internal const string DestructiveTools = "destructive-tools";
    internal const string SensitiveReads = "sensitive-reads";
    internal const string Screenshots = "screenshots";
    internal const string ViewModelInspection = "viewmodel-inspection";
}

internal sealed record McpToolCapabilityEntry(
    Type Type,
    MethodInfo Method,
    McpServerToolAttribute Attribute,
    string Description,
    string Category,
    string[] CapabilityTags,
    string[] PolicyCapabilityTags);

internal static class McpToolCapabilityCatalog
{
    private static readonly Lazy<McpToolCapabilityEntry[]> Entries = new(CreateEntries);

    private static readonly HashSet<string> ScreenshotToolNames = new(StringComparer.Ordinal)
    {
        "element_screenshot"
    };

    private static readonly HashSet<string> SensitiveReadToolNames = new(StringComparer.Ordinal)
    {
        "capture_state_snapshot",
        "compare_trees",
        "clear_dp_value",
        "diagnose_visibility",
        "drain_events",
        "find_binding_leaks",
        "find_elements",
        "get_affected_elements",
        "get_applied_styles",
        "get_binding_errors",
        "get_binding_mismatches",
        "get_binding_value_chain",
        "get_bindings",
        "get_clipping_info",
        "get_dp_metadata",
        "get_dp_value_source",
        "get_element_snapshot",
        "get_event_handlers",
        "get_focus_state",
        "get_form_summary",
        "get_interaction_readiness",
        "get_layout_info",
        "get_logical_tree",
        "get_namescope",
        "get_render_stats",
        "get_resource_chain",
        "get_state_diff",
        "get_template_tree",
        "get_triggers",
        "get_ui_summary",
        "get_validation_errors",
        "get_visual_count",
        "get_visual_tree",
        "get_windows",
        "measure_element_render_time",
        "restore_state_snapshot",
        "serialize_to_xaml",
        "set_dp_value",
        "override_style_setter",
        "trace_routed_events",
        "wait_for_dp_change",
        "wait_for_dp_change_after_mutation",
        "watch_dp_changes"
    };

    private static readonly HashSet<string> ViewModelInspectionToolNames = new(StringComparer.Ordinal)
    {
        "execute_command",
        "get_commands",
        "get_datacontext_chain",
        "get_viewmodel",
        "modify_viewmodel"
    };

    internal static IReadOnlyCollection<McpToolCapabilityEntry> GetEntries()
        => Entries.Value;

    internal static HashSet<string> DiscoverToolNamesWithTag(string capabilityTag)
        => Entries.Value
            .Where(entry => entry.CapabilityTags.Contains(capabilityTag, StringComparer.Ordinal))
            .Select(entry => entry.Attribute.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

    internal static HashSet<string> DiscoverToolNamesWithPolicyTag(string policyTag)
        => Entries.Value
            .Where(entry => entry.PolicyCapabilityTags.Contains(policyTag, StringComparer.Ordinal))
            .Select(entry => entry.Attribute.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

    private static McpToolCapabilityEntry[] CreateEntries()
        => typeof(McpToolCapabilityCatalog).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(GetToolMethods)
            .ToArray();

    private static IEnumerable<McpToolCapabilityEntry> GetToolMethods(Type type)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
            if (string.IsNullOrWhiteSpace(attribute?.Name))
            {
                continue;
            }

            var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
            var category = GetCategory(description);
            var capabilityTags = GetCapabilityTags(attribute, method, category);
            yield return new McpToolCapabilityEntry(
                type,
                method,
                attribute,
                description,
                category,
                capabilityTags,
                GetPolicyCapabilityTags(attribute.Name, capabilityTags));
        }
    }

    private static string[] GetPolicyCapabilityTags(string? toolName, IReadOnlyCollection<string> capabilityTags)
    {
        var policyTags = new SortedSet<string>(StringComparer.Ordinal);
        var name = toolName ?? string.Empty;

        if (capabilityTags.Contains(McpToolCapabilityTags.Destructive, StringComparer.Ordinal)
            && name is not "connect" and not "select_active_process")
        {
            policyTags.Add(McpToolPolicyTags.DestructiveTools);
        }

        if (capabilityTags.Contains(McpToolCapabilityTags.Screenshot, StringComparer.Ordinal))
        {
            policyTags.Add(McpToolPolicyTags.Screenshots);
        }

        if (capabilityTags.Contains(McpToolCapabilityTags.SensitiveRead, StringComparer.Ordinal))
        {
            policyTags.Add(McpToolPolicyTags.SensitiveReads);
        }

        if (capabilityTags.Contains(McpToolCapabilityTags.ViewModel, StringComparer.Ordinal))
        {
            policyTags.Add(McpToolPolicyTags.ViewModelInspection);
        }

        return policyTags.ToArray();
    }

    private static string[] GetCapabilityTags(
        McpServerToolAttribute attribute,
        MethodInfo method,
        string category)
    {
        var tags = new SortedSet<string>(StringComparer.Ordinal)
        {
            category
        };

        if (attribute.ReadOnly)
        {
            tags.Add("read-only");
        }

        if (IsExplicitlyDestructive(method))
        {
            tags.Add(McpToolCapabilityTags.Destructive);
        }

        if (BatchMutationCatalog.SupportedTools.Contains(attribute.Name ?? string.Empty))
        {
            tags.Add("nested-mutation-supported");
        }

        if (string.Equals(attribute.Name, "wait_for_dp_change_after_mutation", StringComparison.Ordinal))
        {
            tags.Add("accepts-mutation-step");
        }

        if (attribute.Name is not "get_processes" and not "list_ui_block_packs" and not "get_ui_block_catalog" and not "validate_ui_blueprint" and not "expand_ui_recipe" and not "render_ui_blueprint" and not "repair_ui_blueprint" and not "apply_ui_blueprint" and not "preview_ui_blueprint")
        {
            tags.Add("requires-target");
        }

        AddNameBasedTags(tags, attribute.Name ?? string.Empty, method.DeclaringType?.Name ?? string.Empty);

        if (attribute.ReadOnly && (tags.Contains("process-discovery") || tags.Contains("scene") || tags.Contains("tree")))
        {
            tags.Add("safe-first");
        }

        return tags.ToArray();
    }

    private static void AddNameBasedTags(SortedSet<string> tags, string toolName, string declaringTypeName)
    {
        if (declaringTypeName.Contains("Process", StringComparison.Ordinal) ||
            toolName is "get_processes" or "connect" or "select_active_process" or "get_active_process" or "ping")
        {
            tags.Add("process-discovery");
        }

        if (ViewModelInspectionToolNames.Contains(toolName))
        {
            tags.Add(McpToolCapabilityTags.ViewModel);
        }

        if (SensitiveReadToolNames.Contains(toolName))
        {
            tags.Add(McpToolCapabilityTags.SensitiveRead);
        }

        if (ScreenshotToolNames.Contains(toolName))
        {
            tags.Add(McpToolCapabilityTags.Screenshot);
        }

        if (toolName.Contains("tree", StringComparison.Ordinal) ||
            toolName is "find_elements" or "compare_trees")
        {
            tags.Add("tree");
            tags.Add("can-be-large");
        }

        if (toolName.Contains("binding", StringComparison.Ordinal) ||
            toolName.Contains("event", StringComparison.Ordinal))
        {
            tags.Add("can-be-large");
        }

        if (declaringTypeName.Contains("Scene", StringComparison.Ordinal) ||
            toolName.Contains("summary", StringComparison.Ordinal) ||
            toolName.Contains("snapshot", StringComparison.Ordinal) ||
            toolName.Contains("visibility", StringComparison.Ordinal) ||
            toolName.Contains("readiness", StringComparison.Ordinal))
        {
            tags.Add("scene");
        }

        if (declaringTypeName.Contains("Performance", StringComparison.Ordinal))
        {
            tags.Add("performance");
        }

        if (declaringTypeName.Contains("UiComposer", StringComparison.Ordinal) ||
            toolName.Contains("ui_block", StringComparison.Ordinal))
        {
            tags.Add("composer");
            tags.Add("ui-pack");
        }

        if (IsStateConsumingTool(toolName))
        {
            tags.Add("state-consuming");
        }

        if (toolName.Contains("text", StringComparison.Ordinal) ||
            toolName.Contains("summary", StringComparison.Ordinal) ||
            toolName.Contains("elements", StringComparison.Ordinal) ||
            toolName.Contains("tree", StringComparison.Ordinal))
        {
            tags.Add("ui-text");
        }
    }

    private static bool IsStateConsumingTool(string toolName)
        => toolName is "batch_mutate"
            or "capture_state_snapshot"
            or "drain_events"
            or "get_state_diff"
            or "restore_state_snapshot";

    private static bool IsExplicitlyDestructive(MethodInfo method)
        => method.GetCustomAttributesData()
            .FirstOrDefault(attribute => attribute.AttributeType == typeof(McpServerToolAttribute))
            ?.NamedArguments.Any(argument =>
                string.Equals(argument.MemberName, "Destructive", StringComparison.Ordinal)
                && argument.TypedValue.Value is true) == true;

    private static string GetCategory(string description)
    {
        const string prefix = "CATEGORY:";
        var index = description.IndexOf(prefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return "uncategorized";
        }

        var start = index + prefix.Length;
        var end = description.IndexOf('\n', start);
        var category = end < 0 ? description[start..] : description[start..end];
        return category.Trim().ToLowerInvariant().Replace(' ', '-');
    }
}
