using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal sealed partial class UiBlueprintApplyService
{
    private const string WpfPresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string SafeSlotBeginValue = " WPFDEVTOOLS_SAFE_SLOT_BEGIN: manual-content ";
    private const string SafeSlotEndValue = " WPFDEVTOOLS_SAFE_SLOT_END: manual-content ";

    private ProjectContentPatchResult AddComposerHeaderAndSafeSlot(
        string blueprintJson,
        string xaml,
        string? existingContent)
    {
        var safeSlot = ReadSafeSlot(existingContent);
        if (!safeSlot.Success)
        {
            return ProjectContentPatchResult.CreateFailure(safeSlot.Error!);
        }

        XDocument document;
        var content = RemoveComposerEnvelope(xaml);
        try
        {
            document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            return ProjectContentPatchResult.CreateFailure(new ApplyBlueprintIssue(
                "$.blueprint",
                "ComposerSafeSlotPlacementFailed",
                $"Rendered XAML could not be prepared for a safe slot: {ex.Message}",
                "Validate the pack renderer output, then rerun the dry-run plan."));
        }

        if (document.Root is null)
        {
            return ProjectContentPatchResult.CreateFailure(CreatePlacementIssue(
                "Rendered XAML has no root element."));
        }

        var baseKind = ComposerWindowRootResolver.ResolveRootBaseKind(registry, blueprintJson, content);
        var placement = PlaceSafeSlot(document, baseKind, safeSlot.Nodes);
        if (!placement.Success)
        {
            return placement;
        }

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(blueprintJson));
        return ProjectContentPatchResult.CreateSuccess(string.Join(
            Environment.NewLine,
            $"{BlueprintHeaderPrefix}{encoded} -->",
            document.ToString(SaveOptions.DisableFormatting),
            string.Empty));
    }

    private static SafeSlotReadResult ReadSafeSlot(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return SafeSlotReadResult.CreateSuccess([]);
        }

        if (CountOccurrences(content, SafeSlotBegin) != 1
            || CountOccurrences(content, SafeSlotEnd) != 1)
        {
            return content.Contains(SafeSlotBegin, StringComparison.Ordinal)
                || content.Contains(SafeSlotEnd, StringComparison.Ordinal)
                ? SafeSlotReadResult.CreateFailure(CreateMalformedSlotIssue())
                : SafeSlotReadResult.CreateSuccess([]);
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return SafeSlotReadResult.CreateFailure(CreateMalformedSlotIssue());
        }

        var begin = document.DescendantNodes().OfType<XComment>()
            .SingleOrDefault(comment => comment.Value == SafeSlotBeginValue);
        var end = document.DescendantNodes().OfType<XComment>()
            .SingleOrDefault(comment => comment.Value == SafeSlotEndValue);
        if (begin?.Parent is null || !ReferenceEquals(begin.Parent, end?.Parent))
        {
            return SafeSlotReadResult.CreateFailure(CreateMalformedSlotIssue());
        }

        var nodes = begin.NodesAfterSelf().TakeWhile(node => !ReferenceEquals(node, end)).ToArray();
        if (nodes.Length == begin.NodesAfterSelf().Count())
        {
            return SafeSlotReadResult.CreateFailure(CreateMalformedSlotIssue());
        }

        PreserveNamespaceContext(begin.Parent, nodes);
        foreach (var node in nodes)
        {
            node.Remove();
        }

        return SafeSlotReadResult.CreateSuccess(nodes);
    }

    private static ProjectContentPatchResult PlaceSafeSlot(
        XDocument document,
        string? baseKind,
        IReadOnlyList<XNode> safeSlotNodes)
    {
        var root = document.Root!;
        var hasClass = root.Attribute(XName.Get("Class", XamlNamespace)) is not null;
        if (string.Equals(baseKind, "resourceDictionary", StringComparison.Ordinal))
        {
            return HasElementContent(safeSlotNodes)
                ? ProjectContentPatchResult.CreateFailure(CreatePlacementIssue(
                    "Manual UI content cannot be hosted by a ResourceDictionary root."))
                : AppendSafeSlot(root, safeSlotNodes);
        }

        if (string.Equals(baseKind, "window", StringComparison.Ordinal))
        {
            return PlaceInSingleContentRoot(root, safeSlotNodes);
        }

        if (!hasClass)
        {
            root.Remove();
            var wrapper = new XElement(XName.Get("Grid", WpfPresentationNamespace), root);
            AppendSafeSlotNodes(wrapper, safeSlotNodes);
            document.Add(wrapper);
            return ProjectContentPatchResult.CreateSuccess(string.Empty);
        }

        if (baseKind is "stackPanel" or "itemsControl" or "tabControl")
        {
            return AppendSafeSlot(root, safeSlotNodes);
        }

        if (baseKind is "contentControl" or "button" or "toggleButton" or "tabItem")
        {
            return PlaceInSingleContentRoot(root, safeSlotNodes);
        }

        return HasElementContent(safeSlotNodes)
            ? ProjectContentPatchResult.CreateFailure(CreatePlacementIssue(
                "The root content model is unknown and cannot safely host preserved manual UI content."))
            : AppendSafeSlot(root, safeSlotNodes);
    }

    private static ProjectContentPatchResult PlaceInSingleContentRoot(
        XElement root,
        IReadOnlyList<XNode> safeSlotNodes)
    {
        if (root.Attributes().Any(attribute => attribute.Name.LocalName == "Content"))
        {
            return HasElementContent(safeSlotNodes)
                ? ProjectContentPatchResult.CreateFailure(CreatePlacementIssue(
                    "The single-content root already declares Content as an attribute."))
                : AppendSafeSlot(root, safeSlotNodes);
        }

        var contentProperties = root.Elements()
            .Where(element => element.Name.LocalName == $"{root.Name.LocalName}.Content")
            .ToArray();
        if (contentProperties.Length > 1)
        {
            return ProjectContentPatchResult.CreateFailure(CreatePlacementIssue(
                "The single-content root declares Content more than once."));
        }

        var contentHost = contentProperties.SingleOrDefault() ?? root;
        var contentElements = contentHost.Elements()
            .Where(element => !ReferenceEquals(contentHost, root) || !element.Name.LocalName.Contains('.', StringComparison.Ordinal))
            .ToArray();
        if (contentHost.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
        {
            return HasElementContent(safeSlotNodes)
                ? ProjectContentPatchResult.CreateFailure(CreatePlacementIssue(
                    "The single-content root uses text content that cannot be combined with preserved manual UI content."))
                : AppendSafeSlot(contentHost, safeSlotNodes);
        }

        if (contentElements.Length == 1 && IsNativeMultiContentContainer(contentElements[0]))
        {
            return AppendSafeSlot(contentElements[0], safeSlotNodes);
        }

        var grid = new XElement(XName.Get("Grid", WpfPresentationNamespace));
        foreach (var element in contentElements)
        {
            element.Remove();
            grid.Add(element);
        }

        AppendSafeSlotNodes(grid, safeSlotNodes);
        contentHost.Add(grid);
        return ProjectContentPatchResult.CreateSuccess(string.Empty);
    }

    private static ProjectContentPatchResult AppendSafeSlot(XContainer container, IReadOnlyList<XNode> nodes)
    {
        AppendSafeSlotNodes(container, nodes);
        return ProjectContentPatchResult.CreateSuccess(string.Empty);
    }

    private static void AppendSafeSlotNodes(XContainer container, IReadOnlyList<XNode> nodes)
    {
        container.Add(new XText(Environment.NewLine), new XComment(SafeSlotBeginValue));
        foreach (var node in nodes)
        {
            container.Add(node);
        }
        container.Add(new XComment(SafeSlotEndValue), new XText(Environment.NewLine));
    }

    private static bool IsNativeMultiContentContainer(XElement element)
        => element.Name.NamespaceName == WpfPresentationNamespace
            && element.Name.LocalName is "Grid" or "StackPanel" or "DockPanel" or "Canvas" or "WrapPanel" or "UniformGrid";

    private static bool HasElementContent(IEnumerable<XNode> nodes)
        => nodes.Any(node => node is XElement or XCData or XProcessingInstruction
            || node is XText text && !string.IsNullOrWhiteSpace(text.Value));

    private static void PreserveNamespaceContext(XContainer parent, IEnumerable<XNode> nodes)
    {
        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parent is XElement element)
        {
            foreach (var ancestor in element.AncestorsAndSelf().Reverse())
            {
                foreach (var attribute in ancestor.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
                {
                    var prefix = attribute.Name.LocalName == "xmlns" ? string.Empty : attribute.Name.LocalName;
                    namespaces[prefix] = attribute.Value;
                }
            }
        }

        foreach (var slotElement in nodes.OfType<XElement>())
        {
            foreach (var (prefix, namespaceUri) in namespaces.Where(pair => pair.Key.Length > 0 && pair.Key != "xml"))
            {
                slotElement.SetAttributeValue(XNamespace.Xmlns + prefix, namespaceUri);
            }
        }
    }

    private static int CountOccurrences(string content, string marker)
    {
        var count = 0;
        var offset = 0;
        while ((offset = content.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += marker.Length;
        }
        return count;
    }

    private static ApplyBlueprintIssue CreateMalformedSlotIssue()
        => new(
            "$.targetPath",
            "MalformedComposerSafeSlot",
            "Existing Composer safe-slot markers must form exactly one ordered pair in the same XAML container.",
            "Restore one complete safe-slot pair or recover the target from its backup, then rerun dry-run.");

    private static ApplyBlueprintIssue CreatePlacementIssue(string reason)
        => new(
            "$.targetPath",
            "ComposerSafeSlotPlacementUnsupported",
            reason,
            "Keep manual UI inside a valid Composer safe slot on a visual container, or let the pack declare an accurate preview baseKind for its root type.");

    private sealed record SafeSlotReadResult(
        bool Success,
        IReadOnlyList<XNode> Nodes,
        ApplyBlueprintIssue? Error)
    {
        public static SafeSlotReadResult CreateSuccess(IReadOnlyList<XNode> nodes) => new(true, nodes, null);
        public static SafeSlotReadResult CreateFailure(ApplyBlueprintIssue error) => new(false, [], error);
    }
}
