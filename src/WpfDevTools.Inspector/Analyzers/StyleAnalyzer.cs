using System.Windows;
using System.Windows.Controls;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Styles and Resources
/// </summary>
public class StyleAnalyzer
{
    /// <summary>
    /// Get applied styles for an element
    /// </summary>
    public object GetAppliedStyles(string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetAppliedStyles(elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not FrameworkElement fe)
        {
            return new { error = "Element is not a FrameworkElement" };
        }

        var styles = new List<object>();

        // Get Style property
        if (fe.Style != null)
        {
            styles.Add(AnalyzeStyle(fe.Style, "Style"));
        }

        return new { styles };
    }

    /// <summary>
    /// Get triggers for an element's style
    /// </summary>
    public object GetTriggers(string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetTriggers(elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not FrameworkElement fe)
        {
            return new { error = "Element is not a FrameworkElement" };
        }

        var triggers = new List<object>();

        // Get triggers from Style
        if (fe.Style != null)
        {
            foreach (var trigger in fe.Style.Triggers)
            {
                triggers.Add(new
                {
                    type = trigger.GetType().Name,
                    // TODO: Add more trigger details
                });
            }
        }

        return new { triggers };
    }

    /// <summary>
    /// Get resource chain for an element
    /// </summary>
    public object GetResourceChain(string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetResourceChain(elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not FrameworkElement fe)
        {
            return new { error = "Element is not a FrameworkElement" };
        }

        var chain = new List<object>();

        // Walk up the tree collecting resources
        var current = fe;
        while (current != null)
        {
            if (current.Resources.Count > 0)
            {
                var resources = new List<object>();
                foreach (var key in current.Resources.Keys)
                {
                    resources.Add(new
                    {
                        key = key.ToString(),
                        type = current.Resources[key]?.GetType().Name
                    });
                }

                chain.Add(new
                {
                    elementType = current.GetType().Name,
                    elementName = current.Name,
                    resourceCount = current.Resources.Count,
                    resources
                });
            }

            current = current.Parent as FrameworkElement;
        }

        // Add Application resources
        if (Application.Current?.Resources.Count > 0)
        {
            var appResources = new List<object>();
            foreach (var key in Application.Current.Resources.Keys)
            {
                appResources.Add(new
                {
                    key = key.ToString(),
                    type = Application.Current.Resources[key]?.GetType().Name
                });
            }

            chain.Add(new
            {
                elementType = "Application",
                elementName = "Application",
                resourceCount = Application.Current.Resources.Count,
                resources = appResources
            });
        }

        return new { chain };
    }

    private object AnalyzeStyle(Style style, string source)
    {
        var setters = new List<object>();

        foreach (var setter in style.Setters)
        {
            if (setter is Setter s)
            {
                setters.Add(new
                {
                    property = s.Property?.Name,
                    value = s.Value?.ToString()
                });
            }
        }

        return new
        {
            source,
            targetType = style.TargetType?.Name,
            setterCount = style.Setters.Count,
            triggerCount = style.Triggers.Count,
            setters
        };
    }

    private DependencyObject? GetRootElement()
    {
        return Application.Current?.MainWindow;
    }

    private DependencyObject? FindElementById(string elementId)
    {
        // TODO: Implement element lookup by ID
        return GetRootElement();
    }
}
