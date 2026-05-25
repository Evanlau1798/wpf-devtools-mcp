using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.McpResources;

internal static class CanonicalMcpToolManifest
{
    public static object GetManifest(string resourceUri)
    {
        var tools = GetToolEntries(resourceUri);

        return new
        {
            resourceUri,
            generatedFrom = nameof(McpServerToolAttribute),
            generatedFromAssembly = typeof(CanonicalMcpToolManifest).Assembly.GetName().Name,
            manifestVersion = "1.0",
            toolCount = tools.Length,
            tools
        };
    }

    private static object[] GetToolEntries(string resourceUri)
    {
        return typeof(CanonicalMcpToolManifest).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(GetToolMethods)
            .OrderBy(entry => entry.Attribute.Name, StringComparer.Ordinal)
            .Select(entry => CreateToolEntry(entry.Type, entry.Method, entry.Attribute, resourceUri))
            .ToArray();
    }

    private static IEnumerable<(Type Type, MethodInfo Method, McpServerToolAttribute Attribute)> GetToolMethods(Type type)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
            if (!string.IsNullOrWhiteSpace(attribute?.Name))
            {
                yield return (type, method, attribute);
            }
        }
    }

    private static object CreateToolEntry(
        Type type,
        MethodInfo method,
        McpServerToolAttribute attribute,
        string resourceUri)
    {
        var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
        var parameters = GetParameters(method);
        var requiredParameters = parameters
            .Where(parameter => parameter.required)
            .Select(parameter => parameter.name)
            .ToArray();
        var capabilityTags = GetCapabilityTags(attribute, method, description);
        var inputHashSource = string.Join("|", parameters.Select(parameter =>
            $"{parameter.name}:{parameter.type}:{parameter.required}:{parameter.defaultValue}:{parameter.description}"));

        return new
        {
            name = attribute.Name,
            title = attribute.Title,
            bridgeFile = $"src/WpfDevTools.Mcp.Server/McpTools/{type.Name}.cs",
            method = $"{type.FullName}.{method.Name}",
            category = GetCategory(description),
            policyCapabilityTags = capabilityTags,
            capabilityTags,
            parameters,
            requiredParameters,
            inputSchemaHash = Sha256(inputHashSource),
            outputSchemaStatus = "generic-structured-payload",
            outputSchemaHash = Sha256("generic-structured-payload:" + resourceUri),
            examplesStatus = description.Contains("EXAMPLES:", StringComparison.Ordinal)
                ? "embedded-description"
                : "missing-description-examples",
            docsCoverageStatus = "docfx-name-level-covered-by-tests",
            annotations = new
            {
                readOnly = attribute.ReadOnly,
                destructive = attribute.Destructive,
                idempotent = attribute.Idempotent,
                openWorld = attribute.OpenWorld
            }
        };
    }

    private static ParameterEntry[] GetParameters(MethodInfo method)
    {
        return method.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(SessionManager))
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .Select(parameter => new ParameterEntry(
                parameter.Name ?? string.Empty,
                GetTypeName(parameter.ParameterType),
                !parameter.HasDefaultValue,
                parameter.HasDefaultValue ? FormatDefaultValue(parameter.DefaultValue) : null,
                parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty))
            .ToArray();
    }

    private static string[] GetCapabilityTags(
        McpServerToolAttribute attribute,
        MethodInfo method,
        string description)
    {
        var tags = new SortedSet<string>(StringComparer.Ordinal)
        {
            GetCategory(description)
        };

        if (attribute.ReadOnly)
        {
            tags.Add("read-only");
        }

        if (attribute.Destructive)
        {
            tags.Add("destructive");
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
            tags.Add("viewmodel");
        }

        if (toolName.Contains("screenshot", StringComparison.Ordinal))
        {
            tags.Add("screenshot");
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

        if (declaringTypeName.Contains("State", StringComparison.Ordinal) ||
            toolName.Contains("state", StringComparison.Ordinal) ||
            toolName.Contains("snapshot", StringComparison.Ordinal))
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

    private static string GetTypeName(Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null)
        {
            return GetTypeName(nullableType) + "?";
        }

        return type.IsArray
            ? GetTypeName(type.GetElementType()!) + "[]"
            : type.Name;
    }

    private static string? FormatDefaultValue(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
    };

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record ParameterEntry(
        string name,
        string type,
        bool required,
        string? defaultValue,
        string description);
}
