using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpTools;

internal static class McpToolCapabilityTags
{
    internal const string Destructive = "destructive";
    internal const string Screenshot = "screenshot";
    internal const string ViewModel = "viewmodel";
}

internal sealed record McpToolCapabilityEntry(
    Type Type,
    MethodInfo Method,
    McpServerToolAttribute Attribute,
    string Description,
    string Category,
    string[] CapabilityTags);

internal static class McpToolCapabilityCatalog
{
    private static readonly Lazy<McpToolCapabilityEntry[]> Entries = new(CreateEntries);

    internal static IReadOnlyCollection<McpToolCapabilityEntry> GetEntries()
        => Entries.Value;

    internal static HashSet<string> DiscoverToolNamesWithTag(string capabilityTag)
        => Entries.Value
            .Where(entry => entry.CapabilityTags.Contains(capabilityTag, StringComparer.Ordinal))
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
            yield return new McpToolCapabilityEntry(
                type,
                method,
                attribute,
                description,
                category,
                GetCapabilityTags(attribute, method, category));
        }
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

        if (!string.Equals(attribute.Name, "get_processes", StringComparison.Ordinal))
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

        if (toolName.Contains("viewmodel", StringComparison.Ordinal) ||
            toolName.Contains("command", StringComparison.Ordinal) ||
            toolName.Contains("datacontext", StringComparison.Ordinal))
        {
            tags.Add(McpToolCapabilityTags.ViewModel);
        }

        if (toolName.Contains("screenshot", StringComparison.Ordinal))
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
