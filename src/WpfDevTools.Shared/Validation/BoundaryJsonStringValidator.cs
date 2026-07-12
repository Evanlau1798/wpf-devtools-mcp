using System.Text.Json;

namespace WpfDevTools.Shared.Validation;

/// <summary>
/// Describes the first oversized string found in a JSON boundary payload.
/// </summary>
/// <param name="DisplayName">Safe property or path name to surface in user-facing validation errors.</param>
/// <param name="Path">Bounded JSON path to the rejected string.</param>
/// <param name="MaxLength">Maximum accepted character count for the property.</param>
public readonly record struct BoundaryStringValidationError(
    string DisplayName,
    string Path,
    int MaxLength);

/// <summary>
/// Applies shared recursive string length limits to MCP arguments and Inspector IPC parameters.
/// </summary>
public static class BoundaryJsonStringValidator
{
    private static readonly HashSet<string> LabelLikeStringProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "automationId",
        "commandName",
        "depthMode",
        "detail",
        "direction",
        "elementName",
        "eventName",
        "interactionType",
        "key",
        "matchMode",
        "mode",
        "nameFilter",
        "outputMode",
        "propertyName",
        "propertyNames",
        "resourceKey",
        "screenshotOutputMode",
        "selectionStrategy",
        "sessionId",
        "sinceTimestamp",
        "snapshotId",
        "snapshotName",
        "statusFilter",
        "trigger",
        "typeMatchMode",
        "typeName",
        "typeNames",
        "viewModelPropertyNames",
        "viewModelType",
        "windowFilter"
    };

    private static readonly HashSet<string> StringifiedJsonProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "blueprintJson",
        "captureSnapshot",
        "triggerMutation"
    };

    /// <summary>
    /// Validates every string contained in a JSON payload against the shared boundary limits.
    /// </summary>
    /// <param name="arguments">The JSON element to validate, or null when the request has no arguments.</param>
    /// <param name="error">The first validation error when the payload is rejected.</param>
    /// <returns>True when the payload is absent or every contained string is within its limit.</returns>
    public static bool TryValidate(JsonElement? arguments, out BoundaryStringValidationError error)
    {
        error = default;
        if (!arguments.HasValue)
        {
            return true;
        }

        return TryValidate(arguments.Value, propertyName: null, path: "$", out error);
    }

    /// <summary>
    /// Resolves the maximum accepted length for a JSON string property name.
    /// </summary>
    /// <param name="propertyName">The containing property name, or null for an unnamed root string.</param>
    /// <returns>The maximum accepted character count for the property.</returns>
    public static int ResolveMaxLength(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return BoundaryStringLimits.MaxStringArgumentLength;
        }

        if (string.Equals(propertyName, "elementId", StringComparison.OrdinalIgnoreCase)
            || string.Equals(propertyName, "elementIds", StringComparison.OrdinalIgnoreCase))
        {
            return BoundaryStringLimits.MaxElementIdLength;
        }

        if (StringifiedJsonProperties.Contains(propertyName!))
        {
            return BoundaryStringLimits.MaxStringifiedJsonArgumentLength;
        }

        return LabelLikeStringProperties.Contains(propertyName!)
            ? BoundaryStringLimits.MaxLabelLength
            : BoundaryStringLimits.MaxStringArgumentLength;
    }

    private static bool TryValidate(
        JsonElement element,
        string? propertyName,
        string path,
        out BoundaryStringValidationError error)
    {
        error = default;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (!TryValidate(
                        property.Value,
                        property.Name,
                        AppendPropertyPath(path, property.Name),
                        out error))
                    {
                        return false;
                    }
                }

                return true;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (!TryValidate(
                        item,
                        propertyName,
                        $"{path}[{index}]",
                        out error))
                    {
                        return false;
                    }

                    index++;
                }

                return true;

            case JsonValueKind.String:
                var value = element.GetString() ?? string.Empty;
                var maxLength = ResolveMaxLength(propertyName);
                if (value.Length <= maxLength)
                {
                    return true;
                }

                var displayName = string.IsNullOrWhiteSpace(propertyName)
                    ? path
                    : propertyName!;
                error = new BoundaryStringValidationError(displayName, path, maxLength);
                return false;

            default:
                return true;
        }
    }

    private static string AppendPropertyPath(string path, string propertyName)
    {
        var safeName = propertyName.Length <= 64
            ? propertyName
            : propertyName.Substring(0, 64) + "...";
        return $"{path}.{safeName}";
    }
}
