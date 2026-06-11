using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfDevTools.Inspector.Analyzers;

internal sealed record InactiveTabActivationGuidance(
    string ActivationPath,
    string TabItemElementId,
    string? TabItemName,
    string? TabControlElementId,
    string? TabControlName);

internal static class InactiveTabActivationGuidanceBuilder
{
    internal static InactiveTabActivationGuidance? TryBuild(
        DependencyObject element,
        Func<DependencyObject, string> elementIdFactory)
    {
        var chain = EnumerateAncestorsAndSelf(element).ToList();
        var inactiveTab = chain.OfType<TabItem>().FirstOrDefault(tab => !tab.IsSelected);
        if (inactiveTab == null)
        {
            return null;
        }

        var tabControl = chain
            .SkipWhile(current => !ReferenceEquals(current, inactiveTab))
            .Skip(1)
            .OfType<TabControl>()
            .FirstOrDefault();

        var pathSegments = new List<string>();
        if (tabControl != null)
        {
            pathSegments.Add(GetNodeLabel(tabControl));
        }

        pathSegments.Add(GetNodeLabel(inactiveTab));

        var activationPath = string.Join(" -> ", pathSegments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
        if (string.IsNullOrWhiteSpace(activationPath))
        {
            activationPath = "TabItem";
        }

        return new InactiveTabActivationGuidance(
            activationPath,
            elementIdFactory(inactiveTab),
            inactiveTab.Name,
            tabControl != null ? elementIdFactory(tabControl) : null,
            tabControl?.Name);
    }

    private static IEnumerable<DependencyObject> EnumerateAncestorsAndSelf(DependencyObject element)
    {
        for (var current = element; current != null; current = GetParent(current))
        {
            yield return current;
        }
    }

    private static string GetNodeLabel(DependencyObject element)
    {
        return element switch
        {
            TabControl tabControl when !string.IsNullOrWhiteSpace(tabControl.Name) => tabControl.Name,
            TabControl => nameof(TabControl),
            TabItem tabItem => GetTabItemLabel(tabItem),
            FrameworkElement frameworkElement when !string.IsNullOrWhiteSpace(frameworkElement.Name) => frameworkElement.Name,
            _ => element.GetType().Name
        };
    }

    private static string GetTabItemLabel(TabItem tabItem)
    {
        if (!string.IsNullOrWhiteSpace(tabItem.Name))
        {
            return tabItem.Name;
        }

        if (tabItem.Header is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        return nameof(TabItem);
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
}
