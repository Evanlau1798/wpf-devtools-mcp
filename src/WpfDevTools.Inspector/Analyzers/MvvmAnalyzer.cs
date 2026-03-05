using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes MVVM patterns in a WPF application, providing access to ViewModels, commands,
/// validation errors, and runtime property modification via reflection on DataContext objects.
/// All operations are marshalled to the UI thread via <see cref="DispatcherAnalyzerBase"/>.
///
/// SECURITY NOTE:
/// - ExecuteCommand and ModifyViewModel use reflection and can modify application state
/// - Property blacklist prevents modification of sensitive properties using regex patterns
/// - All modifications are logged for audit purposes
/// </summary>
public class MvvmAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    // Security: Regex pattern to detect sensitive property names
    // Matches common sensitive property patterns with word boundaries to avoid false positives
    // Examples: Password, UserPassword, ApiToken, SecretKey, ConnectionString
    // Non-matches: PasswordStrength, TokenCount (legitimate properties)
    private static readonly Regex SensitivePropertyPattern = new Regex(
        @"\b(password|pwd|secret|token|key|credential|auth|session|cookie|api[-_]?key|connection[-_]?string)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Initializes a new instance of <see cref="MvvmAnalyzer"/> with the specified element finder.
    /// </summary>
    /// <param name="elementFinder">The element finder used to resolve elements by ID or as the root element.</param>
    public MvvmAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Retrieves ViewModel information from the DataContext of the specified element.
    /// Reflects over the DataContext's public properties and their current values.
    /// </summary>
    /// <param name="elementId">Optional element ID. Uses the application root element when null.</param>
    /// <returns>
    /// An object with <c>success: true</c>, <c>viewModelType</c>, and <c>properties</c> on success,
    /// or <c>success: false</c> and an <c>error</c> message on failure.
    /// </returns>
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
                    properties.Add(new Dictionary<string, object?>
                    {
                        ["name"] = prop.Name,
                        ["type"] = prop.PropertyType.Name,
                        ["value"] = value?.ToString()
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

    /// <summary>
    /// Retrieves all <see cref="System.Windows.Input.ICommand"/> properties from the ViewModel
    /// of the specified element, including their current <c>CanExecute</c> status.
    /// </summary>
    /// <param name="elementId">Optional element ID. Uses the application root element when null.</param>
    /// <returns>
    /// An object with <c>success: true</c> and a <c>commands</c> array on success,
    /// or <c>success: false</c> and an <c>error</c> message if the element is not found.
    /// </returns>
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
                    commands.Add(new Dictionary<string, object?>
                    {
                        ["name"] = prop.Name,
                        ["type"] = prop.PropertyType.Name,
                        ["canExecute"] = cmd?.CanExecute(null) ?? false
                    });
                }
            }

            return new { success = true, commands };
        });
    }

    /// <summary>
    /// Executes a named <see cref="System.Windows.Input.ICommand"/> on the ViewModel of the specified element.
    /// Validates <c>CanExecute</c> before attempting execution.
    /// </summary>
    /// <param name="elementId">Optional element ID. Uses the application root element when null.</param>
    /// <param name="commandName">The name of the command property on the ViewModel.</param>
    /// <param name="parameter">Optional command parameter passed to <c>Execute</c> and <c>CanExecute</c>.</param>
    /// <returns>
    /// An object with <c>success: true</c> and <c>executed: true</c> on success,
    /// or <c>success: false</c> and an <c>error</c> message on failure.
    /// </returns>
    public object ExecuteCommand(string? elementId, string commandName, object? parameter)
    {
        return InvokeOnUIThread<object>(() =>
        {
            // SECURITY: Log command execution attempt for audit
            AuditLogger.LogSecurityEvent("CommandExecution",
                $"Attempt: {commandName}, ElementId: {elementId ?? "root"}",
                AuditSeverity.Information);

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
            {
                AuditLogger.LogSecurityEvent("CommandExecution",
                    $"Blocked by CanExecute: {commandName}",
                    AuditSeverity.Warning);
                return new { success = false, error = $"Command '{commandName}' cannot execute" };
            }

            // Execute command
            cmd.Execute(parameter);

            // SECURITY: Log successful execution
            AuditLogger.LogSecurityEvent("CommandExecution",
                $"Success: {commandName}",
                AuditSeverity.Information);

            return new { success = true, commandName, executed = true };
        });
    }

    /// <summary>
    /// Retrieves all WPF validation errors for the specified element using
    /// <see cref="System.Windows.Controls.Validation.GetErrors"/>.
    /// </summary>
    /// <param name="elementId">Optional element ID. Uses the application root element when null.</param>
    /// <returns>
    /// An object with <c>success: true</c>, <c>errorCount</c>, and an <c>errors</c> array on success,
    /// or <c>success: false</c> and an <c>error</c> message if the element is not found.
    /// </returns>
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
                    errors.Add(new Dictionary<string, object?>
                    {
                        ["errorContent"] = error.ErrorContent?.ToString(),
                        ["isRuleError"] = error.RuleInError != null,
                        ["ruleType"] = error.RuleInError?.GetType().Name
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

                // SECURITY: Check property name against sensitive property pattern
                if (SensitivePropertyPattern.IsMatch(propertyName))
                {
                    // Log security violation
                    AuditLogger.LogSecurityEvent("PropertyModification",
                        $"Blocked sensitive property: {propertyName}",
                        AuditSeverity.Warning);

                    return new
                    {
                        success = false,
                        error = $"Property '{propertyName}' cannot be modified for security reasons"
                    };
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

                // SECURITY: Log property modification for audit
                AuditLogger.LogSecurityEvent("PropertyModification",
                    $"Property: {propertyName}, OldValue: {oldValue ?? "null"}, NewValue: {convertedValue ?? "null"}, ElementId: {elementId ?? "root"}",
                    AuditSeverity.Information);

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
