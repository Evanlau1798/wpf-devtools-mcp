using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF styles, triggers, and templates
/// </summary>
public class StyleAnalyzer
{
    private readonly ElementFinder _elementFinder;

    public StyleAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Get applied styles for an element
    /// </summary>
    public object GetAppliedStyles(string? elementId)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetAppliedStyles(elementId));
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not FrameworkElement fe)
        {
            return new { error = "Element is not a FrameworkElement" };
        }

        var styles = new List<object>();

        // Get explicit style
        if (fe.Style != null)
        {
            styles.Add(new
            {
                type = "Explicit",
                targetType = fe.Style.TargetType?.Name,
                setterCount = fe.Style.Setters.Count,
                triggerCount = fe.Style.Triggers.Count,
                hasBasedOn = fe.Style.BasedOn != null
            });
        }

        return new { success = true, styles, count = styles.Count };
    }

    /// <summary>
    /// Get triggers for an element
    /// </summary>
    public object GetTriggers(string? elementId)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetTriggers(elementId));
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not FrameworkElement fe)
        {
            return new { error = "Element is not a FrameworkElement" };
        }

        var triggers = new List<object>();

        // Get triggers from style
        if (fe.Style != null)
        {
            foreach (var trigger in fe.Style.Triggers)
            {
                triggers.Add(new
                {
                    type = trigger.GetType().Name,
                });
            }
        }

        return new { success = true, triggers, count = triggers.Count };
    }

    /// <summary>
    /// Get template tree for a control
    /// </summary>
    public object GetTemplateTree(string? elementId)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetTemplateTree(elementId));
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not Control control)
        {
            return new { error = "Element is not a Control" };
        }

        if (control.Template == null)
        {
            return new { success = true, message = "Element has no template", hasTemplate = false };
        }

        try
        {
            var templateRoot = control.Template.LoadContent();

            return new
            {
                success = true,
                hasTemplate = true,
                templateType = control.Template.GetType().Name,
                rootType = templateRoot?.GetType().Name
            };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to load template: {ex.Message}" };
        }
    }

    /// <summary>
    /// Get resource resolution chain for an element
    /// </summary>
    public object GetResourceChain(string? elementId, string resourceKey)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetResourceChain(elementId, resourceKey));
        }

        if (string.IsNullOrEmpty(resourceKey))
        {
            return new { success = false, error = "resourceKey is required" };
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        if (element is not FrameworkElement fe)
        {
            return new { error = "Element is not a FrameworkElement" };
        }

        var chain = new List<object>();
        var current = fe;

        // Walk up the tree looking for the resource
        while (current != null)
        {
            if (current.Resources.Contains(resourceKey))
            {
                var resource = current.Resources[resourceKey];
                chain.Add(new
                {
                    level = "Element",
                    elementType = current.GetType().Name,
                    resourceKey,
                    resourceType = resource?.GetType().Name,
                    resourceValue = resource?.ToString()
                });
                break;
            }

            current = current.Parent as FrameworkElement;
        }

        // Check Application resources
        if (chain.Count == 0 && Application.Current?.Resources.Contains(resourceKey) == true)
        {
            var resource = Application.Current.Resources[resourceKey];
            chain.Add(new
            {
                level = "Application",
                elementType = "Application",
                resourceKey,
                resourceType = resource?.GetType().Name,
                resourceValue = resource?.ToString()
            });
        }

        return new
        {
            success = true,
            resourceKey,
            found = chain.Count > 0,
            chain
        };
    }
}
