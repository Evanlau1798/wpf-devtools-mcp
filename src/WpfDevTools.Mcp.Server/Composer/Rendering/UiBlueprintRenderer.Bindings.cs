using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer
{
    private static string ApplyAuthoredBindings(string xaml, int targetPosition, UiBlueprintNode node)
    {
        if (node.Bindings.Count == 0 || targetPosition < 0)
        {
            return xaml;
        }

        var tagStart = xaml.LastIndexOf('<', targetPosition);
        var tagEnd = tagStart < 0 ? -1 : FindTagEnd(xaml, tagStart + 1);
        if (tagStart < 0 || tagEnd < 0)
        {
            return xaml;
        }

        var tag = xaml[tagStart..tagEnd];
        foreach (var propertyName in node.Bindings.Keys.Order(StringComparer.Ordinal))
        {
            tag = BindingAttributePattern(propertyName).Replace(tag, string.Empty);
        }

        var attributes = node.Bindings
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}=\"{EscapeAttribute(pair.Value.GetString() ?? string.Empty)}\"");
        var insertion = " " + string.Join(" ", attributes);
        var insertAt = tag.EndsWith("/", StringComparison.Ordinal) ? tag.Length - 1 : tag.Length;
        tag = tag.Insert(insertAt, insertion);
        return xaml[..tagStart] + tag + xaml[tagEnd..];
    }

    private static Regex BindingAttributePattern(string propertyName) => new(
        $"\\s+{Regex.Escape(propertyName)}\\s*=\\s*(?:\"[^\"]*\"|'[^']*')",
        RegexOptions.CultureInvariant);
}
