using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal static class XamlSafetyScanner
{
    private static readonly Regex UnsafeBindingPattern = new(
        @"\b(?:Source|ElementName|Converter)\s*=|ObjectDataProvider|x:Static",
        RegexOptions.CultureInvariant);
    private static readonly Regex RelativeSourcePattern = new(
        @"RelativeSource\s*=",
        RegexOptions.CultureInvariant);
    private static readonly Regex SafeAncestorBindingPattern = new(
        @"^\{Binding\s+[^,{}]+\s*,\s*RelativeSource\s*=\s*\{RelativeSource\s+AncestorType\s*=\s*\{x:Type\s+[A-Za-z_][A-Za-z0-9_.:-]*\}\s*\}\s*\}$",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> EventNames = new(StringComparer.Ordinal)
    {
        "Checked",
        "Click",
        "Closed",
        "Closing",
        "Drop",
        "GotFocus",
        "Initialized",
        "KeyDown",
        "KeyUp",
        "Loaded",
        "LostFocus",
        "MouseDown",
        "MouseEnter",
        "MouseLeave",
        "MouseMove",
        "MouseUp",
        "MouseWheel",
        "Navigated",
        "PasswordChanged",
        "RequestNavigate",
        "SelectionChanged",
        "TextChanged",
        "Unchecked",
        "Unloaded"
    };

    public static IReadOnlyList<BlueprintValidationIssue> Scan(
        string xaml,
        IReadOnlyList<RenderSourceMapEntry> sourceMap,
        IReadOnlyList<string> allowedResourceDictionarySources)
    {
        var issues = new List<BlueprintValidationIssue>();
        foreach (var tag in EnumerateTags(xaml))
        {
            AddElementIssueIfUnsafe(xaml, tag, sourceMap, allowedResourceDictionarySources, issues);
            foreach (var attribute in tag.Attributes)
            {
                AddAttributeIssueIfUnsafe(tag, attribute, sourceMap, allowedResourceDictionarySources, issues);
            }
        }

        return issues;
    }

    internal static IReadOnlyList<string> ExtractResourceDictionarySources(string xaml)
    {
        var sources = new List<string>();
        foreach (var tag in EnumerateTags(xaml))
        {
            var name = SplitName(tag.Name);
            if (string.Equals(name.LocalName, "ResourceDictionary.Source", StringComparison.Ordinal))
            {
                sources.Add(ReadPropertyElementValue(xaml, tag));
                continue;
            }

            if (!string.Equals(name.LocalName, "ResourceDictionary", StringComparison.Ordinal))
            {
                continue;
            }

            var source = tag.Attributes
                .Where(attribute => string.Equals(
                    SplitName(attribute.Name).LocalName,
                    "Source",
                    StringComparison.Ordinal))
                .Select(attribute => attribute.Value)
                .FirstOrDefault();
            if (source is not null)
            {
                sources.Add(source);
            }
        }

        return sources.Where(source => !string.IsNullOrWhiteSpace(source)).ToArray();
    }

    private static void AddElementIssueIfUnsafe(
        string xaml,
        XamlTag tag,
        IReadOnlyList<RenderSourceMapEntry> sourceMap,
        IReadOnlyList<string> allowedResourceDictionarySources,
        List<BlueprintValidationIssue> issues)
    {
        var name = SplitName(tag.Name);
        if (string.Equals(name.Prefix, "x", StringComparison.Ordinal)
            && IsUnsafeXamlCodeName(name.LocalName))
        {
            issues.Add(Issue(
                ResolveJsonPath(sourceMap, tag.StartIndex),
                "UnsafeXamlClass",
                "Generated renderer XAML must not declare x:Class, x:Code, or x:Subclass.",
                "Remove code-behind declarations from the renderer template."));
        }

        if (IsExecutableElementName(name.LocalName))
        {
            issues.Add(Issue(
                ResolveJsonPath(sourceMap, tag.StartIndex),
                "UnsafeExecutableObject",
                "Generated renderer XAML must not construct executable object providers.",
                "Remove ObjectDataProvider or executable object construction from the renderer template."));
        }

        if (string.Equals(name.LocalName, "ResourceDictionary.Source", StringComparison.Ordinal))
        {
            var source = ReadPropertyElementValue(xaml, tag);
            if (!allowedResourceDictionarySources.Contains(source, StringComparer.Ordinal))
            {
                issues.Add(Issue(
                    ResolveJsonPath(sourceMap, tag.StartIndex),
                    "UnsafeResourceDictionarySource",
                    $"Generated renderer XAML ResourceDictionary Source '{source}' is not declared by the pack.",
                    "Reference only resource dictionaries declared in pack.json resourceSetup.applicationMergedDictionaries."));
            }
        }
    }

    private static void AddAttributeIssueIfUnsafe(
        XamlTag tag,
        XamlAttribute attribute,
        IReadOnlyList<RenderSourceMapEntry> sourceMap,
        IReadOnlyList<string> allowedResourceDictionarySources,
        List<BlueprintValidationIssue> issues)
    {
        var attributeName = SplitName(attribute.Name);
        if (string.Equals(attribute.Name, "xmlns", StringComparison.Ordinal)
            || string.Equals(attributeName.Prefix, "xmlns", StringComparison.Ordinal))
        {
            issues.Add(Issue(
                ResolveJsonPath(sourceMap, attribute.StartIndex),
                "UnsafeXmlNamespace",
                "Renderer fragments must not declare XML namespaces.",
                "Declare namespaces through the Composer-controlled preview or apply host, not inside renderer templates."));
            return;
        }

        if (string.Equals(attributeName.Prefix, "x", StringComparison.Ordinal)
            && IsUnsafeXamlCodeName(attributeName.LocalName))
        {
            issues.Add(Issue(
                ResolveJsonPath(sourceMap, attribute.StartIndex),
                "UnsafeXamlClass",
                "Generated renderer XAML must not declare x:Class, x:Code, or x:Subclass.",
                "Remove code-behind declarations from the renderer template."));
            return;
        }

        if (IsEventAttribute(attributeName.LocalName))
        {
            issues.Add(Issue(
                ResolveJsonPath(sourceMap, attribute.StartIndex),
                "UnsafeEventHandlerAttribute",
                "Generated renderer XAML must not declare event handler attributes.",
                "Use Composer-supported properties, slots, or explicit runtime actions instead of code-behind event handlers."));
            return;
        }

        var tagName = SplitName(tag.Name);
        if (IsResourceDictionarySource(tagName.LocalName, attributeName.LocalName)
            && !allowedResourceDictionarySources.Contains(attribute.Value, StringComparer.Ordinal))
        {
            issues.Add(Issue(
                ResolveJsonPath(sourceMap, attribute.StartIndex),
                "UnsafeResourceDictionarySource",
                $"Generated renderer XAML ResourceDictionary Source '{attribute.Value}' is not declared by the pack.",
                "Reference only resource dictionaries declared in pack.json resourceSetup.applicationMergedDictionaries."));
            return;
        }

        if (IsUnsafeBindingAttribute(tagName.LocalName, attributeName.LocalName, attribute.Value))
        {
            issues.Add(Issue(
                ResolveJsonPath(sourceMap, attribute.StartIndex),
                "UnsafeBindingExpression",
                "Generated renderer XAML binding expressions must not use arbitrary source, converter, or static execution hooks.",
                "Use a simple path binding such as {Binding Rows} or move advanced binding behavior into an explicit Composer contract."));
        }
    }

    private static bool IsUnsafeXamlCodeName(string localName)
        => string.Equals(localName, "Class", StringComparison.Ordinal)
            || string.Equals(localName, "Code", StringComparison.Ordinal)
            || string.Equals(localName, "Subclass", StringComparison.Ordinal);

    private static bool IsExecutableElementName(string localName)
        => string.Equals(localName, "ObjectDataProvider", StringComparison.Ordinal)
            || string.Equals(localName, "ProcessStartInfo", StringComparison.Ordinal)
            || string.Equals(localName, "Process", StringComparison.Ordinal);

    private static bool IsResourceDictionarySource(string tagLocalName, string attributeLocalName)
        => string.Equals(tagLocalName, "ResourceDictionary", StringComparison.Ordinal)
            && string.Equals(attributeLocalName, "Source", StringComparison.Ordinal);

    private static bool IsUnsafeBindingAttribute(string tagLocalName, string attributeLocalName, string value)
    {
        if (value.Contains("{Binding", StringComparison.Ordinal)
            && (UnsafeBindingPattern.IsMatch(value)
                || (RelativeSourcePattern.IsMatch(value) && !SafeAncestorBindingPattern.IsMatch(value))))
        {
            return true;
        }

        return IsBindingElement(tagLocalName)
            && (string.Equals(attributeLocalName, "Source", StringComparison.Ordinal)
                || string.Equals(attributeLocalName, "ElementName", StringComparison.Ordinal)
                || string.Equals(attributeLocalName, "RelativeSource", StringComparison.Ordinal)
                || string.Equals(attributeLocalName, "Converter", StringComparison.Ordinal));
    }

    private static bool IsBindingElement(string tagLocalName)
        => string.Equals(tagLocalName, "Binding", StringComparison.Ordinal)
            || string.Equals(tagLocalName, "MultiBinding", StringComparison.Ordinal)
            || string.Equals(tagLocalName, "PriorityBinding", StringComparison.Ordinal);

    private static bool IsEventAttribute(string localName)
    {
        var candidate = localName.Contains('.')
            ? localName[(localName.LastIndexOf('.') + 1)..]
            : localName;
        if (EventNames.Contains(candidate))
        {
            return true;
        }

        return candidate.StartsWith("Preview", StringComparison.Ordinal)
            && EventNames.Contains(candidate["Preview".Length..]);
    }

    private static string ReadPropertyElementValue(string xaml, XamlTag tag)
    {
        var closeTag = "</" + tag.Name + ">";
        var closeIndex = xaml.IndexOf(closeTag, tag.EndIndex + 1, StringComparison.Ordinal);
        return closeIndex < 0
            ? string.Empty
            : xaml[(tag.EndIndex + 1)..closeIndex].Trim();
    }

    private static IEnumerable<XamlTag> EnumerateTags(string xaml)
    {
        var index = 0;
        while (index < xaml.Length)
        {
            var start = xaml.IndexOf('<', index);
            if (start < 0 || start + 1 >= xaml.Length)
            {
                yield break;
            }

            if (xaml[start + 1] is '/' or '!' or '?')
            {
                index = start + 1;
                continue;
            }

            var end = FindTagEnd(xaml, start + 1);
            if (end < 0)
            {
                yield break;
            }

            var nameStart = SkipWhitespace(xaml, start + 1, end);
            var nameEnd = ReadNameEnd(xaml, nameStart, end);
            if (nameEnd > nameStart)
            {
                var name = xaml[nameStart..nameEnd];
                yield return new XamlTag(
                    name,
                    start,
                    end,
                    ParseAttributes(xaml, nameEnd, end).ToArray());
            }

            index = end + 1;
        }
    }

    private static IEnumerable<XamlAttribute> ParseAttributes(string xaml, int start, int end)
    {
        var index = start;
        while (index < end)
        {
            index = SkipWhitespace(xaml, index, end);
            if (index >= end || xaml[index] == '/')
            {
                yield break;
            }

            var nameStart = index;
            var nameEnd = ReadNameEnd(xaml, nameStart, end);
            if (nameEnd == nameStart)
            {
                yield break;
            }

            index = SkipWhitespace(xaml, nameEnd, end);
            var value = string.Empty;
            if (index < end && xaml[index] == '=')
            {
                index = SkipWhitespace(xaml, index + 1, end);
                (value, index) = ReadAttributeValue(xaml, index, end);
            }

            yield return new XamlAttribute(xaml[nameStart..nameEnd], value, nameStart);
        }
    }

    private static (string Value, int NextIndex) ReadAttributeValue(string xaml, int start, int end)
    {
        if (start >= end)
        {
            return (string.Empty, start);
        }

        if (xaml[start] is '"' or '\'' && FindQuoteEnd(xaml, start + 1, end, xaml[start]) is var quoteEnd && quoteEnd >= 0)
        {
            return (xaml[(start + 1)..quoteEnd], quoteEnd + 1);
        }

        var valueEnd = start;
        while (valueEnd < end && !char.IsWhiteSpace(xaml[valueEnd]) && xaml[valueEnd] != '/')
        {
            valueEnd++;
        }

        return (xaml[start..valueEnd], valueEnd);
    }

    private static int FindTagEnd(string xaml, int start)
    {
        var quote = '\0';
        for (var index = start; index < xaml.Length; index++)
        {
            if (quote != '\0')
            {
                if (xaml[index] == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (xaml[index] is '"' or '\'')
            {
                quote = xaml[index];
            }
            else if (xaml[index] == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindQuoteEnd(string xaml, int start, int end, char quote)
    {
        for (var index = start; index < end; index++)
        {
            if (xaml[index] == quote)
            {
                return index;
            }
        }

        return -1;
    }

    private static int SkipWhitespace(string value, int start, int end)
    {
        var index = start;
        while (index < end && char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        return index;
    }

    private static int ReadNameEnd(string value, int start, int end)
    {
        var index = start;
        while (index < end && IsNameChar(value[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsNameChar(char value)
        => char.IsLetterOrDigit(value)
            || value is '_' or '-' or '.' or ':';

    private static (string Prefix, string LocalName) SplitName(string name)
    {
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        return colon < 0
            ? (string.Empty, name)
            : (name[..colon], name[(colon + 1)..]);
    }

    private static string ResolveJsonPath(IReadOnlyList<RenderSourceMapEntry> sourceMap, int index)
        => sourceMap
            .Where(entry => entry.StartIndex <= index && index < entry.EndIndex)
            .OrderByDescending(entry => entry.JsonPath.Length)
            .Select(entry => entry.JsonPath)
            .FirstOrDefault() ?? "$.layout";

    private static BlueprintValidationIssue Issue(
        string jsonPath,
        string code,
        string message,
        string repairSuggestion)
        => new(jsonPath, code, message, repairSuggestion, [], [], null);

    private readonly record struct XamlTag(
        string Name,
        int StartIndex,
        int EndIndex,
        IReadOnlyList<XamlAttribute> Attributes);

    private readonly record struct XamlAttribute(
        string Name,
        string Value,
        int StartIndex);
}
