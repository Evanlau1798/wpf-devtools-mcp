using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public class MvvmAnalyzer
{
    private readonly ElementFinder _elementFinder;

    public MvvmAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    public object GetViewModel(string? elementId)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => GetViewModel(elementId));
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

        var dataContext = fe.DataContext;
        if (dataContext == null)
        {
            return new { error = "Element has no DataContext" };
        }

        return new
        {
            success = true,
            type = dataContext.GetType().Name,
            data = dataContext
        };
    }

    public object GetCommands(string? elementId)
    {
        // TODO: Implement
        return new { commands = new object[] { } };
    }

    public object ExecuteCommand(string? elementId, string commandName, object? parameter)
    {
        // TODO: Implement
        return new { success = false, error = "Not implemented" };
    }

    public object GetValidationErrors(string? elementId)
    {
        // TODO: Implement
        return new { errors = new object[] { } };
    }

    /// <summary>
    /// Modify ViewModel property at runtime
    /// </summary>
    public object ModifyViewModel(string? elementId, string propertyName, object value)
    {
        // Must run on UI thread
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() =>
                ModifyViewModel(elementId, propertyName, value));
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            return new { success = false, error = "propertyName is required" };
        }

        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return new { success = false, error = "Element not found" };
        }

        if (element is not FrameworkElement fe)
        {
            return new { success = false, error = "Element is not a FrameworkElement" };
        }

        var viewModel = fe.DataContext;
        if (viewModel == null)
        {
            return new { success = false, error = "Element has no DataContext" };
        }

        try
        {
            // Get property info
            var propertyInfo = viewModel.GetType().GetProperty(propertyName);
            if (propertyInfo == null)
            {
                return new { success = false, error = $"Property '{propertyName}' not found on ViewModel" };
            }

            if (!propertyInfo.CanWrite)
            {
                return new { success = false, error = $"Property '{propertyName}' is read-only" };
            }

            // Convert value to target type
            var targetType = propertyInfo.PropertyType;
            object? convertedValue;

            if (value == null)
            {
                convertedValue = null;
            }
            else if (targetType.IsAssignableFrom(value.GetType()))
            {
                convertedValue = value;
            }
            else
            {
                convertedValue = Convert.ChangeType(value, targetType);
            }

            // Get old value
            var oldValue = propertyInfo.GetValue(viewModel);

            // Set new value
            propertyInfo.SetValue(viewModel, convertedValue);

            // Trigger property change notification if ViewModel implements INotifyPropertyChanged
            if (viewModel is System.ComponentModel.INotifyPropertyChanged)
            {
                // The ViewModel should raise PropertyChanged automatically
                // We just report that we modified it
            }

            return new
            {
                success = true,
                message = $"ViewModel property '{propertyName}' modified successfully",
                propertyName,
                oldValue = oldValue?.ToString(),
                newValue = convertedValue?.ToString(),
                viewModelType = viewModel.GetType().Name,
                implementsINotifyPropertyChanged = viewModel is System.ComponentModel.INotifyPropertyChanged
            };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to modify ViewModel property: {ex.Message}" };
        }
    }
}
