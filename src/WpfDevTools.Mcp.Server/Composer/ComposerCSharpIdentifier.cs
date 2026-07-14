using System.Text;

namespace WpfDevTools.Mcp.Server.Composer;

internal static class ComposerCSharpIdentifier
{
    public static string Create(string value, string fallback)
    {
        var builder = new StringBuilder();
        foreach (var ch in value)
        {
            var valid = builder.Length == 0
                ? char.IsLetter(ch) || ch == '_'
                : char.IsLetterOrDigit(ch) || ch == '_';
            builder.Append(valid ? ch : '_');
        }

        return builder.Length == 0 ? fallback : builder.ToString();
    }
}
