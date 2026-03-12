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
            Window window => window.Title,
            TextBlock textBlock => textBlock.Text,
            TextBox textBox => textBox.Text,
            PasswordBox passwordBox => string.IsNullOrEmpty(passwordBox.Password) ? string.Empty : "[password]",
            HeaderedContentControl headeredContentControl => headeredContentControl.Header?.ToString(),
            HeaderedItemsControl headeredItemsControl => headeredItemsControl.Header?.ToString(),
            ContentControl contentControl => contentControl.Content?.ToString(),
            ComboBox comboBox => comboBox.Text,
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
            blockers.Add("NoLayoutSize");
        }

        if (element is ButtonBase button && button.Command != null && !button.Command.CanExecute(button.CommandParameter))
        {
            blockers.Add("CommandCannotExecute");
        }

        return (blockers.Count == 0, blockers);
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

        return string.IsNullOrWhiteSpace(rawText)
            ? null
            : rawText.Trim().TrimEnd(':');
    }

    internal static bool ShouldOmitSemanticNode(
        FrameworkElement element,
        string kind,
        string? text,
        object? currentValue,
        IReadOnlyList<string> annotations)
    {
        if (kind != "text")
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return currentValue == null && annotations.Count == 0;
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
        => LogicalTreeHelper.GetParent(element)
            ?? (element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : null);

    private static bool ContainsIgnoreCase(string value, string expected)
        => value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
}
