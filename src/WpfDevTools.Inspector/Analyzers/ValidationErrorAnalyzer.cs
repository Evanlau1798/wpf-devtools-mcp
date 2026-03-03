using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF Validation Errors
/// </summary>
public class ValidationErrorAnalyzer
{
    /// <summary>
    /// Get all validation errors from element or tree
    /// </summary>
    public object GetValidationErrors(string? elementId = null)
    {
        // Must run on UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetValidationErrors(elementId));
        }

        var element = elementId == null
            ? GetRootElement()
            : FindElementById(elementId);

        if (element == null)
        {
            return new { error = "Element not found" };
        }

        var errors = new List<object>();

        // Collect validation errors from element and descendants
        CollectValidationErrors(element, errors);

        return new { errors };
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

    private void CollectValidationErrors(DependencyObject element, List<object> errors)
    {
        // Check Validation.Errors attached property
        if (element is UIElement uiElement)
        {
            var validationErrors = Validation.GetErrors(uiElement);
            foreach (var error in validationErrors)
            {
                errors.Add(new
                {
                    elementType = element.GetType().Name,
                    elementName = (element as FrameworkElement)?.Name,
                    errorContent = error.ErrorContent?.ToString(),
                    exception = error.Exception?.Message
                });
            }
        }

        // Check IDataErrorInfo
        if (element is FrameworkElement fe && fe.DataContext is IDataErrorInfo dataErrorInfo)
        {
            var errorMessage = dataErrorInfo.Error;
            if (!string.IsNullOrEmpty(errorMessage))
            {
                errors.Add(new
                {
                    elementType = element.GetType().Name,
                    elementName = fe.Name,
                    errorContent = errorMessage,
                    source = "IDataErrorInfo"
                });
            }
        }

        // Check INotifyDataErrorInfo
        if (element is FrameworkElement fe2 && fe2.DataContext is INotifyDataErrorInfo notifyDataErrorInfo)
        {
            if (notifyDataErrorInfo.HasErrors)
            {
                // Get all property errors
                var allErrors = notifyDataErrorInfo.GetErrors(null);
                if (allErrors != null)
                {
                    foreach (var error in allErrors)
                    {
                        errors.Add(new
                        {
                            elementType = element.GetType().Name,
                            elementName = fe2.Name,
                            errorContent = error?.ToString(),
                            source = "INotifyDataErrorInfo"
                        });
                    }
                }
            }
        }

        // Recursively check children
        var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(element, i);
            CollectValidationErrors(child, errors);
        }
    }
}
