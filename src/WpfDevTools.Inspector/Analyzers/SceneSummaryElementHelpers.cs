using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace WpfDevTools.Inspector.Analyzers;

internal static class SceneSummaryElementHelpers
{
    internal static bool IsSemanticElement(DependencyObject element)
    {
        return element is Window
            or TextBlock
            or TextBox
            or PasswordBox
            or ButtonBase
            or ComboBox
            or ComboBoxItem
            or ListBox
            or ListBoxItem
            or CheckBox
            or RadioButton
            or Slider
            or ProgressBar
            or Label
            or TabItem;
    }

    internal static bool IsInputControl(DependencyObject element)
    {
        return element is TextBox
            or PasswordBox
            or ComboBox
            or CheckBox
            or RadioButton;
    }

    internal static bool IsCommandControl(DependencyObject element) => element is ButtonBase;

    internal static bool ShouldIncludeFormSummaryElement(FrameworkElement element, bool includeFramework)
    {
        return includeFramework || !IsFrameworkNoiseFormElement(element);
    }

    internal static IEnumerable<DependencyObject> GetVisualChildren(DependencyObject element)
    {
        if (element is not Visual and not System.Windows.Media.Media3D.Visual3D)
        {
            yield break;
        }

        var count = VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < count; index++)
        {
            yield return VisualTreeHelper.GetChild(element, index);
        }
    }

    internal static IEnumerable<DependencyObject> GetLogicalChildren(DependencyObject element)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(element))
        {
            if (child is DependencyObject dependencyObject)
            {
                yield return dependencyObject;
            }
        }
    }

    internal static IReadOnlyList<DependencyObject> GetSceneChildren(DependencyObject element)
    {
        var children = new List<DependencyObject>();
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);

        foreach (var child in GetVisualChildren(element))
        {
            if (seen.Add(child))
            {
                children.Add(child);
            }
        }

        foreach (var child in GetLogicalChildren(element))
        {
            if (seen.Add(child))
            {
                children.Add(child);
            }
        }

        return children;
    }

    internal static int GetNextTraversalDepth(
        DependencyObject element,
        int currentDepth,
        SceneTraversalDepthMode depthMode)
    {
        if (depthMode == SceneTraversalDepthMode.Visual)
        {
            return currentDepth + 1;
        }

        return IsSemanticElement(element)
            ? currentDepth + 1
            : currentDepth;
    }

    internal static string? GetElementName(DependencyObject element) =>
        element is FrameworkElement frameworkElement && !string.IsNullOrWhiteSpace(frameworkElement.Name)
            ? frameworkElement.Name
            : null;

    internal static string GetKind(DependencyObject element)
    {
        return element switch
        {
            Window => "window",
            TextBox or PasswordBox => "input",
            ComboBox or CheckBox or RadioButton => "selection",
            ButtonBase => "action",
            TextBlock or Label => "text",
            Slider or ProgressBar => "value",
            ListBox or ListBoxItem or ComboBoxItem or TabItem => "collection",
            _ => "semantic"
        };
    }

    internal static string? GetDisplayText(DependencyObject element)
    {
        return element switch
        {
            Window window => NormalizeDisplayText(window.Title),
            TextBlock textBlock => NormalizeDisplayText(textBlock.Text),
            TextBox textBox => NormalizeDisplayText(textBox.Text),
            PasswordBox passwordBox => string.IsNullOrEmpty(passwordBox.Password) ? string.Empty : "[password]",
            HeaderedContentControl headeredContentControl => NormalizeDisplayObject(headeredContentControl.Header),
            HeaderedItemsControl headeredItemsControl => NormalizeDisplayObject(headeredItemsControl.Header),
            ContentControl contentControl => NormalizeDisplayObject(contentControl.Content),
            ComboBox comboBox => NormalizeDisplayText(comboBox.Text),
            _ => null
        };
    }

    internal static object? GetCurrentValue(DependencyObject element)
    {
        return element switch
        {
            TextBox textBox => textBox.Text,
            PasswordBox passwordBox => string.IsNullOrEmpty(passwordBox.Password) ? string.Empty : "[password]",
            ComboBox comboBox => comboBox.SelectedItem?.ToString() ?? comboBox.Text,
            CheckBox checkBox => checkBox.IsChecked,
            RadioButton radioButton => radioButton.IsChecked,
            _ => null
        };
    }

    internal static IReadOnlyList<string> GetAnnotations(FrameworkElement element)
    {
        var annotations = new List<string>();
        if (!element.IsEnabled)
        {
            annotations.Add("disabled");
        }

        if (element.Visibility != Visibility.Visible)
        {
            annotations.Add($"visibility:{element.Visibility}");
        }

        if (element.Opacity <= 0)
        {
            annotations.Add("transparent");
        }

        if (Validation.GetHasError(element))
        {
            annotations.Add($"validation:{Validation.GetErrors(element).Count}");
        }

        return annotations;
    }

    internal static (bool isReady, IReadOnlyList<string> blockers) EvaluateInteractionReadiness(FrameworkElement element)
    {
        var blockers = new List<string>();
        if (!element.IsEnabled)
        {
            blockers.Add("ElementDisabled");
        }

        if (element.Visibility != Visibility.Visible)
        {
            blockers.Add("ElementHidden");
        }

        if (element.Opacity <= 0)
        {
            blockers.Add("ElementTransparent");
        }

        if (!element.IsHitTestVisible)
        {
            blockers.Add("HitTestingDisabled");
        }

        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            blockers.Add(GetLayoutSizeBlockerReason(element));
        }

        if (element is ButtonBase button && button.Command != null && !button.Command.CanExecute(button.CommandParameter))
        {
            blockers.Add("CommandCannotExecute");
        }

        return (blockers.Count == 0, blockers);
    }

    internal static string GetLayoutSizeBlockerReason(FrameworkElement element)
    {
        return IsInsideInactiveTab(element)
            ? "ElementInInactiveTab"
            : "NoLayoutSize";
    }

    internal static bool IsInsideInactiveTab(DependencyObject element)
    {
        for (var current = GetParent(element); current != null; current = GetParent(current))
        {
            if (current is TabItem tabItem && !tabItem.IsSelected)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsPrimaryCommand(ButtonBase button)
    {
        var candidate = $"{button.Name} {button.Content}".Trim();
        return ContainsIgnoreCase(candidate, "save")
            || ContainsIgnoreCase(candidate, "submit")
            || ContainsIgnoreCase(candidate, "confirm")
            || ContainsIgnoreCase(candidate, "ok");
    }

    internal static string? TryGetNearbyLabel(FrameworkElement element)
    {
        if (element.Parent is Grid grid)
        {
            var row = Grid.GetRow(element);
            var column = Grid.GetColumn(element);
            foreach (var child in grid.Children.OfType<FrameworkElement>())
            {
                if (ReferenceEquals(child, element))
                {
                    continue;
                }

                if (Grid.GetRow(child) == row && Grid.GetColumn(child) < column && TryExtractLabelText(child) is { } gridLabel)
                {
                    return gridLabel;
                }
            }
        }

        if (element.Parent is Panel panel)
        {
            var index = panel.Children.IndexOf(element);
            for (var current = index - 1; current >= 0; current--)
            {
                if (panel.Children[current] is FrameworkElement sibling && TryExtractLabelText(sibling) is { } panelLabel)
                {
                    return panelLabel;
                }
            }
        }

        return TryGetAncestorHeaderLabel(element);
    }

    internal static string? TryGetBindingPath(FrameworkElement element)
    {
        if (element is TextBox textBox)
        {
            return BindingOperations.GetBinding(textBox, TextBox.TextProperty)?.Path?.Path;
        }

        if (element is ComboBox comboBox)
        {
            return BindingOperations.GetBinding(comboBox, Selector.SelectedItemProperty)?.Path?.Path
                ?? BindingOperations.GetBinding(comboBox, ComboBox.TextProperty)?.Path?.Path;
        }

        if (element is ToggleButton toggleButton)
        {
            return BindingOperations.GetBinding(toggleButton, ToggleButton.IsCheckedProperty)?.Path?.Path;
        }

        return null;
    }

    private static bool IsFrameworkNoiseFormElement(FrameworkElement element)
    {
        if (!IsKnownFrameworkNoiseFormType(element))
        {
            return false;
        }

        return !HasMeaningfulFormSignal(element);
    }

    private static bool IsKnownFrameworkNoiseFormType(FrameworkElement element)
    {
        return element is RepeatButton
            || element.GetType().Name is "DataGridRowHeader" or "DataGridColumnHeader";
    }

    private static bool HasMeaningfulFormSignal(FrameworkElement element)
    {
        var elementName = GetElementName(element);
        if (elementName is string candidateName
            && !string.IsNullOrWhiteSpace(candidateName)
            && !candidateName.StartsWith("PART_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if ((element.TemplatedParent == null && !string.IsNullOrWhiteSpace(TryGetNearbyLabel(element)))
            || !string.IsNullOrWhiteSpace(TryGetBindingPath(element))
            || Validation.GetHasError(element))
        {
            return true;
        }

        return HasMeaningfulDisplayText(GetDisplayText(element));
    }

    private static bool HasMeaningfulDisplayText(string? displayText)
    {
        if (displayText is null)
        {
            return false;
        }

        var trimmedText = displayText.Trim();
        if (trimmedText.Length == 0)
        {
            return false;
        }

        return !trimmedText.StartsWith("System.", StringComparison.Ordinal);
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

    internal static bool ShouldOmitSemanticNode(
        FrameworkElement element,
        string kind,
        string? text,
        object? currentValue,
        IReadOnlyList<string> annotations)
    {
        if (element is Window)
        {
            return false;
        }

        if (annotations.Count > 0 || currentValue != null)
        {
            return false;
        }

        if (kind == "text")
        {
            return !HasMeaningfulDisplayText(text);
        }

        if (HasMeaningfulDisplayText(text))
        {
            return false;
        }

        if (GetElementName(element) is not string elementName
            || string.IsNullOrWhiteSpace(elementName))
        {
            return true;
        }

        return elementName.StartsWith("PART_", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeDisplayObject(object? value)
    {
        return value switch
        {
            null => null,
            string text => NormalizeDisplayText(text),
            FrameworkElement => null,
            _ => NormalizeDisplayText(value.ToString())
        };
    }

    private static string? NormalizeDisplayText(string? text)
    {
        if (!HasMeaningfulDisplayText(text))
        {
            return null;
        }

        return text!.Trim();
    }

    private static string? TryGetAncestorHeaderLabel(FrameworkElement element)
    {
        for (var current = GetParent(element); current != null; current = GetParent(current))
        {
            if (current is FrameworkElement frameworkElement
                && TryExtractLabelText(frameworkElement) is { } headerLabel)
            {
                return headerLabel;
            }
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        if (element is FrameworkElement frameworkElement)
        {
            if (frameworkElement.Parent != null)
            {
                return frameworkElement.Parent;
            }

            if (frameworkElement.TemplatedParent != null)
            {
                return frameworkElement.TemplatedParent;
            }
        }

        if (element is FrameworkContentElement frameworkContentElement)
        {
            if (frameworkContentElement.Parent != null)
            {
                return frameworkContentElement.Parent;
            }

            if (frameworkContentElement.TemplatedParent != null)
            {
                return frameworkContentElement.TemplatedParent;
            }
        }

        return LogicalTreeHelper.GetParent(element)
            ?? (element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : null);
    }

    private static bool ContainsIgnoreCase(string value, string expected)
        => value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
}
