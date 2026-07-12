using System.Globalization;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal static class UiBlockPropertyValueRules
{
    internal static bool IsValid(JsonElement value, UiBlockProperty property)
    {
        if (!MatchesType(value, property.Type))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            if (property.Minimum is double minimum && number < minimum
                || property.Maximum is double maximum && number > maximum
                || property.Integer && number != Math.Truncate(number))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(property.Format)
            && value.ValueKind == JsonValueKind.String
            && !MatchesFormat(value.GetString() ?? string.Empty, property.Format))
        {
            return false;
        }

        var allowedValues = property.AllowedValues.Length > 0 ? property.AllowedValues : property.EnumValues;
        return allowedValues.Length == 0
            || value.ValueKind != JsonValueKind.String
            || allowedValues.Contains(value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    internal static bool MatchesType(JsonElement value, string type)
        => type switch
        {
            "binding" or "string" => value.ValueKind == JsonValueKind.String,
            "boolean" or "bool" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "number" => value.ValueKind == JsonValueKind.Number,
            "object" => value.ValueKind == JsonValueKind.Object,
            _ => false
        };

    internal static bool MatchesFormat(string value, string format)
        => format switch
        {
            "thickness" => IsThickness(value),
            "gridLength" => IsGridLength(value),
            _ => false
        };

    private static bool IsThickness(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length is 1 or 2 or 4
            && parts.All(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
    }

    private static bool IsGridLength(string value)
    {
        if (string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase) || value == "*")
        {
            return true;
        }

        var number = value.EndsWith('*') ? value[..^1] : value;
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0;
    }
}
