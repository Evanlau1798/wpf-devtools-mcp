using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static class ExistingXamlContractAnalyzer
{
    internal const int MaximumChanges = 128;

    internal static ExistingXamlContractAnalysis Analyze(
        string existingXaml,
        string proposedXaml,
        string? codeBehind)
    {
        try
        {
            var existing = NamedElements(XDocument.Parse(existingXaml));
            var proposed = NamedElements(XDocument.Parse(proposedXaml));
            var handlers = CodeBehindMethods(codeBehind);
            var changes = new List<ExistingXamlContractChange>();

            foreach (var (name, element) in existing.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (!proposed.TryGetValue(name, out var replacement))
                {
                    Add(changes, new(
                        "ExistingNamedElementRemoved", name, element.Name.LocalName, null, null, null,
                        $"Named element '{name}' would be removed from the generated XAML."));
                    continue;
                }

                if (element.Name != replacement.Name)
                {
                    Add(changes, new(
                        "ExistingNamedElementTypeChanged", name, element.Name.LocalName,
                        replacement.Name.LocalName, null, null,
                        $"Named element '{name}' would change type from '{element.Name.LocalName}' to '{replacement.Name.LocalName}'."));
                }

                foreach (var attribute in EventContracts(element, handlers))
                {
                    var preserved = replacement.Attributes().Any(candidate =>
                        candidate.Name.LocalName == attribute.Name.LocalName
                        && candidate.Value == attribute.Value);
                    if (!preserved)
                    {
                        Add(changes, new(
                            "ExistingEventHandlerRemoved", name, element.Name.LocalName,
                            replacement.Name.LocalName, attribute.Name.LocalName, attribute.Value,
                            $"Event contract {attribute.Name.LocalName}=\"{attribute.Value}\" on '{name}' would be removed."));
                    }
                }
            }

            var truncated = changes.Count > MaximumChanges;
            return new ExistingXamlContractAnalysis(
                changes.Take(MaximumChanges).ToArray(),
                truncated,
                AnalysisAvailable: true);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            return new ExistingXamlContractAnalysis(
                [new ExistingXamlContractChange(
                    "ExistingContractAnalysisUnavailable", null, null, null, null, null,
                    "Existing XAML contracts could not be compared because one XAML document is not parseable.")],
                Truncated: false,
                AnalysisAvailable: false);
        }
    }

    private static Dictionary<string, XElement> NamedElements(XDocument document)
        => document.DescendantsAndSelf()
            .Select(element => (Element: element, Name: ElementName(element)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Element, StringComparer.Ordinal);

    private static string? ElementName(XElement element)
        => element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value;

    private static HashSet<string> CodeBehindMethods(string? codeBehind)
        => string.IsNullOrWhiteSpace(codeBehind)
            ? []
            : Regex.Matches(codeBehind, @"\b([A-Za-z_]\w*)\s*\(")
                .Select(match => match.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<XAttribute> EventContracts(
        XElement element,
        IReadOnlySet<string> handlers)
        => element.Attributes().Where(attribute =>
            attribute.Name.LocalName != "Name"
            && handlers.Contains(attribute.Value));

    private static void Add(
        ICollection<ExistingXamlContractChange> changes,
        ExistingXamlContractChange change)
    {
        if (changes.Count <= MaximumChanges)
        {
            changes.Add(change);
        }
    }
}

internal sealed record ExistingXamlContractAnalysis(
    IReadOnlyList<ExistingXamlContractChange> Changes,
    bool Truncated,
    bool AnalysisAvailable)
{
    internal static readonly ExistingXamlContractAnalysis NotApplicable = new([], false, true);
}

internal sealed record ExistingXamlContractChange(
    string Code,
    string? ElementName,
    string? ExistingType,
    string? ProposedType,
    string? EventName,
    string? HandlerName,
    string Message);

internal static class XDocumentTraversal
{
    internal static IEnumerable<XElement> DescendantsAndSelf(this XDocument document)
        => document.Root is null ? [] : document.Root.DescendantsAndSelf();
}
