using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public class MvvmAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    public MvvmAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    public object GetViewModel(string? elementId)
    {
        return InvokeOnUIThread<object>(() =>
        {
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

            var dataContext = fe.DataContext;
            if (dataContext == null)
            {
                return new { success = false, error = "Element has no DataContext" };
            }

            var dcType = dataContext.GetType();
            var properties = new List<object>();
            foreach (var prop in dcType.GetProperties())
            {
                try
                {
                    var value = prop.GetValue(dataContext);
                    properties.Add(new
                    {
                        name = prop.Name,
                        type = prop.PropertyType.Name,
                        value = value?.ToString()
                    });
                }
                catch (Exception) { /* Skip properties with throwing getters */ }
            }

            return new
            {
                success = true,
                viewModelType = dcType.Name,
                properties
            };
        });
    }

    public object GetCommands(string? elementId)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId != null ? _elementFinder.FindById(elementId) : GetRootElement();
            if (element == null)
                return new { success = false, error = "Element not found" };

            var fe = element as FrameworkElement;
            if (fe?.DataContext == null)
                return new { success = true, commands = new object[] { } };

            var commands = new List<object>();
            var dcType = fe.DataContext.GetType();
            foreach (var prop in dcType.GetProperties())
            {
                if (typeof(System.Windows.Input.ICommand).IsAssignableFrom(prop.PropertyType))
                {
                    var cmd = prop.GetValue(fe.DataContext) as System.Windows.Input.ICommand;
                    commands.Add(new
                    {
                        name = prop.Name,
                        type = prop.PropertyType.Name,
                        canExecute = cmd?.CanExecute(null) ?? false
                    });
                }
            }

            return new { success = true, commands };
        });
    }

    public object ExecuteCommand(string? elementId, string commandName, object? parameter)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId != null ? _elementFinder.FindById(elementId) : GetRootElement();
            if (element == null)
                return new { success = false, error = "Element not found" };

            var fe = element as FrameworkElement;
            if (fe?.DataContext == null)
                return new { success = false, error = "No DataContext found" };

            var prop = fe.DataContext.GetType().GetProperty(commandName);
            if (prop == null)
                return new { success = false, error = $"Command '{commandName}' not found" };

            var cmd = prop.GetValue(fe.DataContext) as System.Windows.Input.ICommand;
            if (cmd == null)
                return new { success = false, error = $"'{commandName}' is not an ICommand" };

            if (!cmd.CanExecute(parameter))
                return new { success = false, error = $"Command '{commandName}' cannot execute" };

            cmd.Execute(parameter);
            return new { success = true, commandName, executed = true };
        });
    }

    public object GetValidationErrors(string? elementId)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId != null ? _elementFinder.FindById(elementId) : GetRootElement();
            if (element == null)
                return new { success = false, error = "Element not found" };

            var errors = new List<object>();
            if (element is DependencyObject depObj)
            {
                var validationErrors = Validation.GetErrors(depObj);
                foreach (var error in validationErrors)
                {
                    errors.Add(new
                    {
                        errorContent = error.ErrorContent?.ToString(),
                        isRuleError = error.RuleInError != null,
                        ruleType = error.RuleInError?.GetType().Name
                    });
                }
            }

            return new { success = true, errorCount = errors.Count, errors };
        });
    }

    private DependencyObject? GetRootElement()
    {
        return _elementFinder.GetRootElement();
    }

    /// <summary>
    /// Modify ViewModel property at runtime
    /// </summary>
    public object ModifyViewModel(string? elementId, string propertyName, object value)
    {
        return InvokeOnUIThread<object>(() =>
        {
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
                    // Check if target type accepts null
                    if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                    {
                        return new
                        {
                            success = false,
                            error = $"Cannot assign null to non-nullable type '{targetType.Name}'"
                        };
                    }
                    convertedValue = null;
                }
                else if (targetType.IsAssignableFrom(value.GetType()))
                {
                    convertedValue = value;
                }
                else
                {
                    try
                    {
                        // Handle Nullable<T>
                        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                        // Handle Enum types
                        if (underlyingType.IsEnum && value is string strValue)
                        {
                            convertedValue = Enum.Parse(underlyingType, strValue, ignoreCase: true);
                        }
                        else
                        {
                            convertedValue = ConvertValue(value, underlyingType);
                        }

                        // If target is Nullable<T>, wrap the result
                        if (targetType.IsGenericType &&
                            targetType.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                            convertedValue != null)
                        {
                            convertedValue = Activator.CreateInstance(targetType, convertedValue);
                        }
                    }
                    catch (Exception conversionEx)
                    {
                        return new
                        {
                            success = false,
                            error = $"Type conversion failed: {conversionEx.Message}",
                            sourceType = value.GetType().Name,
                            targetType = targetType.Name,
                            hint = "Ensure the value is compatible with the property type"
                        };
                    }
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
        });
    }
}
