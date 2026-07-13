using System.Net;
using System.Text.RegularExpressions;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer
{
    private static readonly Regex RootNamePattern = new(
        "(?:^|\\s)(?:x:Name|Name)\\s*=\\s*(?:\"(?<double>[^\"]+)\"|'(?<single>[^']+)')",
        RegexOptions.CultureInvariant);

    private static string AddTransientElementCorrelation(
        string xaml,
        string jsonPath,
        string blockKind,
        List<RenderElementCorrelation> correlations)
    {
        var rootStart = FindRootElementStart(xaml);
        var rootEnd = rootStart < 0 ? -1 : FindTagEnd(xaml, rootStart + 1);
        if (rootEnd < 0)
        {
            return xaml;
        }

        var rootTag = xaml[rootStart..rootEnd];
        var existingName = RootNamePattern.Match(rootTag);
        var elementName = existingName.Success
            ? WebUtility.HtmlDecode(existingName.Groups["double"].Success
                ? existingName.Groups["double"].Value
                : existingName.Groups["single"].Value)
            : $"WpfDevToolsBp_{correlations.Count:D4}";
        if (!existingName.Success)
        {
            var insertAt = rootEnd > rootStart && xaml[rootEnd - 1] == '/' ? rootEnd - 1 : rootEnd;
            xaml = xaml[..insertAt] + $" x:Name=\"{elementName}\"" + xaml[insertAt..];
        }

        correlations.Add(new RenderElementCorrelation(elementName, jsonPath, blockKind));
        return xaml;
    }
}
