using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Templates (ControlTemplate and DataTemplate)
/// </summary>
public class TemplateAnalyzer
{
    /// <summary>
    /// Get template tree for an element
    /// </summary>
    public object GetTemplateTree(string? elementId = null, int? maxDepth = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetTemplateTree(elementId, maxDepth));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { success = false, error = "Element not found" };
        }

        // Check for ControlTemplate
        if (element is Control control && control.Template != null)
        {
            var templateRoot = control.Template.LoadContent() as FrameworkElement;
            if (templateRoot != null)
            {
                var tree = WalkTemplateTree(templateRoot, maxDepth ?? int.MaxValue, 0);
                return new
                {
                    templateType = "ControlTemplate",
                    targetType = control.Template.TargetType?.Name,
                    tree
                };
            }
        }

        // Check for DataTemplate
        if (element is ContentPresenter contentPresenter && contentPresenter.ContentTemplate != null)
        {
            var templateRoot = contentPresenter.ContentTemplate.LoadContent() as FrameworkElement;
            if (templateRoot != null)
            {
                var tree = WalkTemplateTree(templateRoot, maxDepth ?? int.MaxValue, 0);
                return new
                {
                    templateType = "DataTemplate",
                    tree
                };
            }
        }

        return new { success = false, error = "No template found for element" };
    }

    /// <summary>
    /// Override a style setter value
    /// </summary>
    public object OverrideStyleSetter(string propertyName, object value, string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => OverrideStyleSetter(propertyName, value, elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { success = false, error = "Element not found" };
        }

        if (element is not FrameworkElement fe)
        {
            return new { success = false, error = "Element is not a FrameworkElement" };
        }

        try
        {
            // Create a new style based on the existing one
            if (fe.Style != null)
            {
                var newStyle = new Style(fe.Style.TargetType, fe.Style);

                // Find the property
                var dp = FindDependencyProperty(fe, propertyName);
                if (dp == null)
                {
                    return new { success = false, error = $"Property '{propertyName}' not found" };
                }

                // Add or update setter
                newStyle.Setters.Add(new Setter(dp, value));
                fe.Style = newStyle;

                return new { success = true, message = $"Style setter for '{propertyName}' overridden successfully" };
            }

            return new { success = false, error = "Element has no style to override" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to override style setter: {ex.Message}" };
        }
    }

    private object WalkTemplateTree(DependencyObject element, int maxDepth, int currentDepth)
    {
        if (currentDepth >= maxDepth)
        {
            return new { type = element.GetType().Name, childCount = VisualTreeHelper.GetChildrenCount(element) };
        }

        var children = new List<object>();
        var childCount = VisualTreeHelper.GetChildrenCount(element);

        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            children.Add(WalkTemplateTree(child, maxDepth, currentDepth + 1));
        }

        return new
        {
            type = element.GetType().Name,
            name = (element as FrameworkElement)?.Name,
            childCount = childCount,
            children = children.Count > 0 ? children : null
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

    private DependencyProperty? FindDependencyProperty(DependencyObject element, string propertyName)
    {
        var type = element.GetType();
        var fieldName = propertyName + "Property";
        var field = type.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        if (field != null && field.FieldType == typeof(DependencyProperty))
        {
            return field.GetValue(null) as DependencyProperty;
        }

        return null;
    }
}
