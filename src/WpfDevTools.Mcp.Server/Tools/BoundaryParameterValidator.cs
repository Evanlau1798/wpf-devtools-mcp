using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.Tools;

internal static class BoundaryParameterValidator
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
        "selectionStrategy",
        "sessionId",
        "sinceTimestamp",
        "snapshotId",
        "snapshotName",
        "statusFilter",
        "trigger",
        "typeName",
        "typeNames",
        "viewModelPropertyNames",
        "viewModelType",
        "windowFilter"
    };

    private static readonly HashSet<string> StringifiedJsonProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "captureSnapshot",
        "triggerMutation"
    };

    internal static bool TryValidateStringBoundaries(JsonElement? arguments, out object? error)
    {
        error = null;
        if (!arguments.HasValue)
        {
            return true;
        }

        return TryValidateStringBoundaries(arguments.Value, propertyName: null, path: "$", out error);
    }

    internal static bool TryGetOptionalStringEnum(
        JsonElement? arguments,
        string propertyName,
        string defaultValue,
        string[] allowedValues,
        out string value,
        out object? error)
    {
        value = defaultValue;
        if (!TryGetOptionalString(arguments, propertyName, out var rawValue, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        var trimmedValue = rawValue.Trim();
        var match = allowedValues.FirstOrDefault(allowedValue =>
            string.Equals(allowedValue, trimmedValue, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            value = match;
            return true;
        }

        error = CreateInvalidArgument(
            $"{propertyName} must be one of: {string.Join(", ", allowedValues)}.",
            $"Omit {propertyName} for the default '{defaultValue}', or use one of: {string.Join(", ", allowedValues)}.");
        return false;
    }

    internal static bool TryGetOptionalIntInRange(
        JsonElement? arguments,
        string propertyName,
        int minimum,
        int maximum,
        out int? value,
        out object? error)
    {
        value = null;
        if (!arguments.HasValue || !arguments.Value.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            error = null;
            return true;
        }

        if (!TryReadInt(property, out var parsedValue))
        {
            error = CreateInvalidArgument(
                $"{propertyName} must be an integer when provided.",
                $"Provide {propertyName} as an integer between {minimum} and {maximum}.");
            return false;
        }

        if (parsedValue < minimum || parsedValue > maximum)
        {
            error = CreateInvalidArgument(
                $"{propertyName} must be between {minimum} and {maximum}.",
                $"Provide {propertyName} as an integer between {minimum} and {maximum}.");
            return false;
        }

        value = parsedValue;
        error = null;
        return true;
    }

    private static bool TryGetOptionalString(
        JsonElement? arguments,
        string propertyName,
        out string? value,
        out object? error)
    {
        value = null;
        if (!arguments.HasValue || !arguments.Value.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            error = null;
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = CreateInvalidArgument(
                $"{propertyName} must be a string when provided.",
                $"Provide {propertyName} as a JSON string value.");
            return false;
        }

        value = property.GetString();
        error = null;
        return true;
    }

    private static bool TryValidateStringBoundaries(
        JsonElement element,
        string? propertyName,
        string path,
        out object? error)
    {
        error = null;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (!TryValidateStringBoundaries(
                        property.Value,
                        property.Name,
                        $"{path}.{property.Name}",
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
                    if (!TryValidateStringBoundaries(
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

                var displayName = string.IsNullOrWhiteSpace(propertyName) ? path : propertyName;
                error = CreateInvalidArgument(
                    $"{displayName} exceeds the maximum length of {maxLength} characters.",
                    $"Shorten {displayName}; oversized strings are rejected before the request is forwarded to the target process.");
                return false;

            default:
                return true;
        }
    }

    private static int ResolveMaxLength(string? propertyName)
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

        if (StringifiedJsonProperties.Contains(propertyName))
        {
            return BoundaryStringLimits.MaxStringifiedJsonArgumentLength;
        }

        return LabelLikeStringProperties.Contains(propertyName)
            ? BoundaryStringLimits.MaxLabelLength
            : BoundaryStringLimits.MaxStringArgumentLength;
    }

    private static bool TryReadInt(JsonElement property, out int value)
    {
        value = 0;
        if (property.ValueKind == JsonValueKind.Number)
        {
            try
            {
                value = property.GetInt32();
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return false;
    }

    private static ToolErrorPayload CreateInvalidArgument(string message, string hint)
        => new()
        {
            Error = message,
            ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
            Hint = hint
        };
}
