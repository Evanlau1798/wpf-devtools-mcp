using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class ConnectTool
{
    private static object? TryGetOptionalString(
        JsonElement? arguments,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!arguments.HasValue || !arguments.Value.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return new
            {
                success = false,
                error = $"{propertyName} must be a string when provided",
                errorCode = "InvalidArgument",
                hint = $"Provide {propertyName} as a JSON string value."
            };
        }

        value = property.GetString();
        return null;
    }
}
