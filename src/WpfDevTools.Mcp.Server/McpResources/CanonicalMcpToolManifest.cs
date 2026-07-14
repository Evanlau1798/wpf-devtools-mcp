using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            $"{parameter.name}:{parameter.type}:{parameter.required}:{parameter.defaultValue}:{parameter.description}:{FormatConstraints(parameter.constraints)}"));
        var toolName = attribute.Name!;
        var outputSchemaStatus = McpToolOutputSchemas.GetSchemaStatus(toolName);
        var mutationRestoreRequirementStatus = GetMutationRestoreRequirementStatus(entry);

        return new
        {
            name = toolName,
            title = attribute.Title,
            bridgeFile = $"src/WpfDevTools.Mcp.Server/McpTools/{type.Name}.cs",
            method = $"{type.FullName}.{method.Name}",
            category = entry.Category,
            riskTier = GetRiskTier(entry),
            policyTags = entry.PolicyCapabilityTags,
            policyCapabilityTags = entry.PolicyCapabilityTags,
            capabilityTags = entry.CapabilityTags,
            parameters,
            requiredParameters,
            inputSchemaHash = Sha256(inputHashSource),
            outputSchemaStatus,
            responseContractStatus = outputSchemaStatus,
            outputSchemaHash = Sha256(McpToolOutputSchemas.GetSchemaHashSource(toolName)),
            examplesStatus = description.Contains("EXAMPLES:", StringComparison.Ordinal)
                ? "embedded-description"
                : "missing-description-examples",
            docsCoverageStatus = "docfx-name-level-covered-by-tests",
            liveTestCoverageStatus = McpToolLiveTestCoverageCatalog.GetStatus(toolName),
            mutationRestoreRequirementStatus,
            annotations = new
            {
                readOnly = attribute.ReadOnly,
                destructive = attribute.Destructive,
                idempotent = attribute.Idempotent,
                openWorld = attribute.OpenWorld
            }
        };
    }

    private static string GetRiskTier(McpToolCapabilityEntry entry)
    {
        var policyTags = entry.PolicyCapabilityTags;

        if (policyTags.Contains(McpToolPolicyTags.Screenshots, StringComparer.Ordinal))
        {
            return "controlled-sensitive";
        }

        var isDestructive = policyTags.Contains(McpToolPolicyTags.DestructiveTools, StringComparer.Ordinal);
        var hasSensitiveSurface =
            policyTags.Contains(McpToolPolicyTags.SensitiveReads, StringComparer.Ordinal)
            || policyTags.Contains(McpToolPolicyTags.ViewModelInspection, StringComparer.Ordinal)
            || entry.CapabilityTags.Contains("state-consuming", StringComparer.Ordinal);

        if (isDestructive && hasSensitiveSurface)
        {
            return "destructive-sensitive";
        }

        if (isDestructive)
        {
            return "destructive";
        }

        if (hasSensitiveSurface)
        {
            return "sensitive-read";
        }

        return "low";
    }

    private static string GetMutationRestoreRequirementStatus(McpToolCapabilityEntry entry)
    {
        var toolName = entry.Attribute.Name ?? string.Empty;

        if (toolName is "connect" or "select_active_process")
        {
            return "session-state-only";
        }

        return entry.PolicyCapabilityTags.Contains(McpToolPolicyTags.DestructiveTools, StringComparer.Ordinal)
            ? "snapshot-restore-required"
            : "not-mutating";
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
                parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
                GetConstraints(parameter)))
            .ToArray();
    }

    private static ParameterConstraints? GetConstraints(ParameterInfo parameter)
    {
        var stringLength = parameter.GetCustomAttribute<StringLengthAttribute>();
        var minLength = parameter.GetCustomAttribute<MinLengthAttribute>()?.Length
                        ?? stringLength?.MinimumLength;
        var maxLength = parameter.GetCustomAttribute<MaxLengthAttribute>()?.Length
                        ?? stringLength?.MaximumLength;
        var range = parameter.GetCustomAttribute<RangeAttribute>();
        var allowedValues = parameter.GetCustomAttribute<AllowedValuesAttribute>()?.Values;
        return minLength is null && maxLength is null && range is null && allowedValues is null
            ? null
            : new ParameterConstraints(minLength, maxLength, range?.Minimum, range?.Maximum, allowedValues);
    }

    private static string FormatConstraints(ParameterConstraints? constraints)
    {
        if (constraints is null)
        {
            return string.Empty;
        }

        var baseValue = $"{constraints.minLength}:{constraints.maxLength}:{constraints.minimum}:{constraints.maximum}";
        return constraints.allowedValues is null
            ? baseValue
            : $"{baseValue}:allowedValues={JsonSerializer.Serialize(constraints.allowedValues)}";
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
        string description,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ParameterConstraints? constraints);

    private sealed record ParameterConstraints(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? minLength,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? maxLength,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? minimum,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? maximum,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object?[]? allowedValues);
}
