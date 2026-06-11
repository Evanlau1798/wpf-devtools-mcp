using System.Windows.Media;

namespace WpfDevTools.Inspector.Utilities;

/// <summary>
/// Helper methods for serializing WPF property values
/// </summary>
public static class SerializationHelper
{
    /// <summary>
    /// Serialize a property value to a JSON-friendly format
    /// </summary>
    /// <param name="value">Value to serialize</param>
    /// <returns>Serialized value suitable for JSON, or null if value is null</returns>
    public static object? SerializePropertyValue(object? value)
    {
        if (value == null)
            return null;

        var type = value.GetType();

        // Simple types - return as-is
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            return value;

        // Brush types
        if (value is SolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            return $"SolidColorBrush: #{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        if (value is Brush brush)
        {
            return $"{brush.GetType().Name}";
        }

        return value.ToString();
    }
}
