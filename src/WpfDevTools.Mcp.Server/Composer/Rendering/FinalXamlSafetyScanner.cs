using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using WpfDevTools.Mcp.Server.Composer.Blueprints;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal static partial class FinalXamlSafetyScanner
{
    private const string XamlLanguageNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly HashSet<string> UnsafeDirectiveNames = new(StringComparer.Ordinal)
    {
        "Arguments",
        "Class",
        "ClassAttributes",
        "Code",
        "FactoryMethod",
        "Members",
        "Static",
        "StaticExtension",
        "Subclass",
        "TypeArguments"
    };

    private static readonly HashSet<string> UriConsumingMembers = new(StringComparer.Ordinal)
    {
        "Application.StartupUri",
        "BitmapImage.UriSource",
        "Frame.Source",
        "Hyperlink.NavigateUri",
        "Image.Source",
        "ImageBrush.ImageSource",
        "ImageDrawing.ImageSource",
        "MediaElement.Source",
        "NavigationWindow.Source",
        "SoundPlayerAction.Source",
        "WebBrowser.Source",
        "Window.Icon"
    };

    [GeneratedRegex(@"\{\s*(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*):Static(?:Extension)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex StaticExtensionPattern();

    [GeneratedRegex(
        @"(?:^|,)\s*(?:FallbackValue|TargetNullValue)\s*=\s*(?<value>'[^']*'|""[^""]*""|[^,}]*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex BindingUriFallbackPattern();

    public static IReadOnlyList<BlueprintValidationIssue> Scan(string xaml)
    {
        XDocument document;
        try
        {
            using var stringReader = new StringReader(xaml);
            using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 10 * 1024 * 1024
            });
            document = XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return
            [
                Issue(
                    "InvalidGeneratedXaml",
                    "Generated renderer output is not well-formed XML.",
                    "Repair the renderer template so the final assembled XAML is well-formed.")
            ];
        }

        if (document.Root is null)
        {
            return
            [
                Issue(
                    "InvalidGeneratedXaml",
                    "Generated renderer output does not contain a root element.",
                    "Repair the renderer template so the final assembled XAML has one root element.")
            ];
        }

        var issues = new List<BlueprintValidationIssue>();
        foreach (var element in document.Root.DescendantsAndSelf())
        {
            AddUnsafeDirectiveIssue(element.Name, issues);
            AddUnsafeUriElementIssue(element, issues);

            foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
            {
                AddUnsafeDirectiveIssue(attribute.Name, issues);
                AddUnsafeStaticExtensionIssue(element, attribute.Value, issues);
                AddUnsafeUriAttributeIssue(element, attribute, issues);
            }

            foreach (var text in element.Nodes().OfType<XText>())
            {
                AddUnsafeStaticExtensionIssue(element, text.Value, issues);
            }
        }

        return issues
            .DistinctBy(issue => (issue.Code, issue.Message), EqualityComparer<(string, string)>.Default)
            .ToArray();
    }

    private static void AddUnsafeDirectiveIssue(
        XName name,
        ICollection<BlueprintValidationIssue> issues)
    {
        if (string.Equals(name.NamespaceName, XamlLanguageNamespace, StringComparison.Ordinal)
            && UnsafeDirectiveNames.Contains(name.LocalName))
        {
            issues.Add(Issue(
                "UnsafeXamlDirective",
                $"Generated XAML must not use the XAML language directive '{name.LocalName}'.",
                "Remove code-behind and object-construction directives from the renderer template."));
        }
    }

    private static void AddUnsafeStaticExtensionIssue(
        XElement context,
        string value,
        ICollection<BlueprintValidationIssue> issues)
    {
        foreach (Match match in StaticExtensionPattern().Matches(value))
        {
            var prefix = match.Groups["prefix"].Value;
            if (string.Equals(
                    context.GetNamespaceOfPrefix(prefix)?.NamespaceName,
                    XamlLanguageNamespace,
                    StringComparison.Ordinal))
            {
                issues.Add(Issue(
                    "UnsafeXamlMarkupExtension",
                    "Generated XAML must not use the XAML language Static markup extension.",
                    "Replace static member access with an inert literal, binding, or explicit Composer contract."));
            }
        }
    }

    private static void AddUnsafeUriElementIssue(
        XElement element,
        ICollection<BlueprintValidationIssue> issues)
    {
        if (element.HasElements
            || !UriConsumingMembers.Contains(element.Name.LocalName)
            || IsSafeNonLiteralOrApplicationResource(element.Value))
        {
            return;
        }

        issues.Add(UnsafeUriIssue(element.Name.LocalName));
    }

    private static void AddUnsafeUriAttributeIssue(
        XElement element,
        XAttribute attribute,
        ICollection<BlueprintValidationIssue> issues)
    {
        var member = attribute.Name.LocalName.Contains('.', StringComparison.Ordinal)
            ? attribute.Name.LocalName
            : element.Name.LocalName + "." + attribute.Name.LocalName;
        if (!UriConsumingMembers.Contains(member)
            || IsSafeNonLiteralOrApplicationResource(attribute.Value))
        {
            return;
        }

        issues.Add(UnsafeUriIssue(member));
    }

    private static bool IsSafeNonLiteralOrApplicationResource(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrEmpty(trimmed)
            || (trimmed.StartsWith("{Binding", StringComparison.Ordinal)
                && !HasUnsafeBindingUriFallback(trimmed))
            || trimmed.StartsWith("{StaticResource", StringComparison.Ordinal)
            || trimmed.StartsWith("{DynamicResource", StringComparison.Ordinal)
            || PreviewResourcePolicy.IsApplicationLocalPackSource(trimmed);
    }

    private static bool HasUnsafeBindingUriFallback(string binding)
        => BindingUriFallbackPattern()
            .Matches(binding)
            .Select(match => match.Groups["value"].Value.Trim().Trim('\'', '"'))
            .Any(value => !string.IsNullOrEmpty(value)
                && !value.StartsWith("{StaticResource", StringComparison.Ordinal)
                && !value.StartsWith("{DynamicResource", StringComparison.Ordinal)
                && !PreviewResourcePolicy.IsApplicationLocalPackSource(value));

    private static BlueprintValidationIssue UnsafeUriIssue(string member)
        => Issue(
            "UnsafePreviewUri",
            $"Generated XAML must not assign a non-local literal URI to {member}.",
            "Use a binding without literal URI fallbacks, a trusted resource reference, or an application-local pack URI.");

    private static BlueprintValidationIssue Issue(
        string code,
        string message,
        string repairSuggestion)
        => new("$.layout", code, message, repairSuggestion, [], [], null);
}
