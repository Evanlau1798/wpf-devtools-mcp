using System.Text.Json;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

internal static class BoundaryParameterValidator
{
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