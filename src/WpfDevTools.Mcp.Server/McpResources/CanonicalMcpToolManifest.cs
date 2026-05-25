using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpTools;

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
        return McpToolCapabilityCatalog.GetEntries()
            .OrderBy(entry => entry.Attribute.Name, StringComparer.Ordinal)
            .Select(entry => CreateToolEntry(entry, resourceUri))
            .ToArray();
    }

    private static object CreateToolEntry(
        McpToolCapabilityEntry entry,
        string resourceUri)
    {
        var type = entry.Type;
        var method = entry.Method;
        var attribute = entry.Attribute;
        var description = entry.Description;
        var parameters = GetParameters(method);
        var requiredParameters = parameters
            .Where(parameter => parameter.required)
            .Select(parameter => parameter.name)
            .ToArray();
        var inputHashSource = string.Join("|", parameters.Select(parameter =>
            $"{parameter.name}:{parameter.type}:{parameter.required}:{parameter.defaultValue}:{parameter.description}"));

        return new
        {
            name = attribute.Name,
            title = attribute.Title,
            bridgeFile = $"src/WpfDevTools.Mcp.Server/McpTools/{type.Name}.cs",
            method = $"{type.FullName}.{method.Name}",
            category = entry.Category,
            policyCapabilityTags = entry.CapabilityTags,
            capabilityTags = entry.CapabilityTags,
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
