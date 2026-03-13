using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Navigation;

public static class NavigationParamBuilders
{
    public static JsonElement Create(params (string name, object? value)[] parameters)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters)
        {
            if (!string.IsNullOrWhiteSpace(name) && value is not null)
            {
                values[name] = value;
            }
        }

        return JsonSerializer.SerializeToElement(values);
    }
}
