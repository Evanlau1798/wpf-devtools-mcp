using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace WpfDevTools.Inspector.Analyzers;

internal static partial class SceneSummaryElementHelpers
{
    internal static string? TryGetNearbyLabel(FrameworkElement element)
    {
        if (TryGetGridLabelCandidate(element) is { } gridCandidate)
        {
            return gridCandidate.LabelText;
        }

        if (TryGetPanelLabelCandidate(element) is { } panelCandidate)
        {
            if (!ShouldDisambiguatePanelLabel(panelCandidate.Panel, panelCandidate.LabelIndex))
            {
                return panelCandidate.LabelText;
            }

            return CombineWithIdentifier(panelCandidate.LabelText, element);
        }

        if (TryGetAncestorHeaderCandidate(element) is { } ancestorCandidate)
        {
            if (!ShouldDisambiguateAncestorHeader(ancestorCandidate.HeaderOwner))
            {
                return ancestorCandidate.LabelText;
            }

            return CombineWithIdentifier(ancestorCandidate.LabelText, element);
        }

        return null;
    }

    private static string CombineWithIdentifier(string baseLabel, FrameworkElement element)
    {
        var identifier = TryGetIdentifierFallbackLabel(element);
        return string.IsNullOrWhiteSpace(identifier)
            ? baseLabel
            : $"{baseLabel} / {identifier}";
    }

    private static (string LabelText, Panel Panel, int LabelIndex)? TryGetPanelLabelCandidate(FrameworkElement element)
    {
        if (element.Parent is not Panel panel)
        {
            return null;
        }

        var index = panel.Children.IndexOf(element);
        for (var current = index - 1; current >= 0; current--)
        {
            if (panel.Children[current] is FrameworkElement sibling
                && TryExtractLabelText(sibling) is { } panelLabel)
            {
                return (panelLabel, panel, current);
            }
        }

        return null;
    }

    private static bool ShouldDisambiguatePanelLabel(Panel panel, int labelIndex)
    {
        var unlabeledInputCount = 0;
        for (var current = labelIndex + 1; current < panel.Children.Count; current++)
        {
            if (panel.Children[current] is not FrameworkElement sibling)
            {
                continue;
            }

            if (TryExtractLabelText(sibling) is not null)
            {
                break;
            }

            if (IsInputControl(sibling) && TryGetGridLabelCandidate(sibling) is null)
            {
                unlabeledInputCount++;
            }
        }

        return unlabeledInputCount > 1;
    }

    private static (string LabelText, FrameworkElement HeaderOwner)? TryGetAncestorHeaderCandidate(FrameworkElement element)
    {
        for (var current = GetParent(element); current != null; current = GetParent(current))
        {
            if (current is FrameworkElement frameworkElement
                && TryExtractLabelText(frameworkElement) is { } headerLabel)
            {
                return (headerLabel, frameworkElement);
            }
        }

        return null;
    }

    private static bool ShouldDisambiguateAncestorHeader(FrameworkElement headerOwner)
    {
        var unlabeledInputCount = EnumerateDescendantFrameworkElements(headerOwner)
            .Count(candidate => IsInputControl(candidate) && TryGetExplicitNearbyLabel(candidate) is null);

        return unlabeledInputCount > 1;
    }

    private static string? TryGetExplicitNearbyLabel(FrameworkElement element)
    {
        return TryGetGridLabelCandidate(element)?.LabelText
            ?? TryGetPanelLabelCandidate(element)?.LabelText;
    }

    private static (string LabelText, FrameworkElement LabelElement)? TryGetGridLabelCandidate(FrameworkElement element)
    {
        if (element.Parent is not Grid grid)
        {
            return null;
        }

        var row = Grid.GetRow(element);
        var column = Grid.GetColumn(element);
        foreach (var child in grid.Children.OfType<FrameworkElement>())
        {
            if (ReferenceEquals(child, element))
            {
                continue;
            }

            if (Grid.GetRow(child) == row
                && Grid.GetColumn(child) < column
                && TryExtractLabelText(child) is { } gridLabel)
            {
                return (gridLabel, child);
            }
        }

        return null;
    }

    private static IEnumerable<FrameworkElement> EnumerateDescendantFrameworkElements(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        return EnumerateDescendantFrameworkElementsCore(root, visited);
    }

    private static IEnumerable<FrameworkElement> EnumerateDescendantFrameworkElementsCore(
        DependencyObject root,
        HashSet<DependencyObject> visited)
    {
        if (!visited.Add(root))
        {
            yield break;
        }

        if (root is FrameworkElement frameworkElement)
        {
            yield return frameworkElement;
        }

        foreach (var child in GetSceneChildren(root))
        {
            foreach (var descendant in EnumerateDescendantFrameworkElementsCore(child, visited))
            {
                yield return descendant;
            }
        }
    }

    private static string? TryGetIdentifierFallbackLabel(FrameworkElement element)
    {
        var bindingPath = TryGetBindingPath(element);
        if (!string.IsNullOrWhiteSpace(bindingPath))
        {
            var bindingLabel = HumanizeIdentifier(bindingPath!.Split('.').Last());
            if (!string.IsNullOrWhiteSpace(bindingLabel))
            {
                return bindingLabel;
            }
        }

        return HumanizeIdentifier(GetElementName(element));
    }

    private static string? HumanizeIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var trimmedIdentifier = identifier!.Trim();
        foreach (var suffix in new[] { "TextBox", "PasswordBox", "ComboBox", "CheckBox", "RadioButton", "ToggleButton", "Button" })
        {
            if (trimmedIdentifier.EndsWith(suffix, StringComparison.Ordinal))
            {
                trimmedIdentifier = trimmedIdentifier.Substring(0, trimmedIdentifier.Length - suffix.Length);
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(trimmedIdentifier))
        {
            return null;
        }

        var builder = new StringBuilder(trimmedIdentifier.Length + 8);
        for (var index = 0; index < trimmedIdentifier.Length; index++)
        {
            var current = trimmedIdentifier[index];
            if (index > 0 && ShouldInsertWordBoundary(trimmedIdentifier[index - 1], current))
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString().Trim();
    }

    private static bool ShouldInsertWordBoundary(char previous, char current)
    {
        return char.IsLower(previous) && char.IsUpper(current)
            || char.IsLetter(previous) && char.IsDigit(current)
            || char.IsDigit(previous) && char.IsLetter(current);
    }

    private static string? TryExtractLabelText(FrameworkElement element)
    {
        var rawText = element switch
        {
            TextBlock textBlock => textBlock.Text,
            Label label => label.Content?.ToString(),
            HeaderedContentControl headeredContentControl => headeredContentControl.Header?.ToString(),
            HeaderedItemsControl headeredItemsControl => headeredItemsControl.Header?.ToString(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        var trimmedText = rawText!.Trim().TrimEnd(':');
        return string.IsNullOrWhiteSpace(trimmedText)
            ? null
            : trimmedText;
    }
}
