using System.Windows.Media;

namespace WpfDevTools.Inspector.Utilities;

public static class SerializationHelper
{
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
