using System.Xml.Linq;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal static class PreviewResourcePolicy
{
    private const string ApplicationPackPrefix = "pack://application:,,,/";

    public static bool IsApplicationLocalPackSource(string source)
    {
        var value = source.Trim();
        var path = value.StartsWith(ApplicationPackPrefix, StringComparison.OrdinalIgnoreCase)
            ? value[ApplicationPackPrefix.Length..]
            : value.StartsWith("/", StringComparison.Ordinal) ? value[1..] : string.Empty;
        return path.Length > 0
            && !path.StartsWith("/", StringComparison.Ordinal)
            && !path.Contains(':')
            && !value.Contains("..", StringComparison.Ordinal)
            && !value.Contains('\\')
            && !value.Contains('?')
            && !value.Contains('#')
            && !value.Any(char.IsWhiteSpace);
    }

    public static string NormalizeApplicationResource(string resource)
    {
        if (resource.TrimStart().StartsWith('<'))
        {
            return resource;
        }

        if (!IsApplicationLocalPackSource(resource))
        {
            throw new InvalidDataException("Preview resources must be inline XAML or application-local pack URIs.");
        }

        return new XElement("ResourceDictionary", new XAttribute("Source", resource.Trim()))
            .ToString(SaveOptions.DisableFormatting);
    }
}
