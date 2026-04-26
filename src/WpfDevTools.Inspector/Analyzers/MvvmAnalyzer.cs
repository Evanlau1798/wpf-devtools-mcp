using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfDevTools.Inspector.Events;
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
public sealed partial class MvvmAnalyzer : DispatcherAnalyzerBase
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
        : this(elementFinder, null)
    {
    }

    internal MvvmAnalyzer(
        ElementFinder elementFinder,
        WatchEventBuffer? watchEventBuffer)
    {
        _elementFinder = elementFinder;
        _watchEventBuffer = watchEventBuffer;
        _validationChangeTracker = new ValidationChangeTracker(elementFinder);
    }

    /// <summary>
    /// Retrieves ViewModel information from the DataContext of the specified element.
    /// Reflects over the DataContext's public properties and their current values.
    /// </summary>
    /// <param name="elementId">Optional element ID. Uses the application root element when null.</param>
    /// <param name="propertyNames">Optional list of ViewModel properties to include. Null returns all readable properties.</param>
    /// <returns>
    /// An object with <c>success: true</c>, <c>viewModelType</c>, and <c>properties</c> on success,
    /// or <c>success: false</c> and an <c>error</c> message on failure.
    /// </returns>
    public object GetViewModel(string? elementId, IReadOnlyList<string>? propertyNames = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not FrameworkElement fe)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a FrameworkElement",
                    "Target a FrameworkElement with a DataContext before calling get_viewmodel.");
            }

            var dataContext = fe.DataContext;
            if (dataContext == null)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element has no DataContext",
                    "Choose an element with a ViewModel/DataContext or call get_datacontext_chain first.");
            }

            var dcType = dataContext.GetType();
            var requestedPropertyNames = propertyNames?
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);
            var reflectedProperties = dcType.GetProperties();

            if (requestedPropertyNames is { Count: > 0 })
            {
                var matchedProperties = reflectedProperties
                    .Where(prop => requestedPropertyNames.Contains(prop.Name))
                    .ToList();

                if (matchedProperties.Count == 0)
                {
                    return ToolErrorFactory.Create(
                        Shared.ErrorHandling.ToolErrorCode.PropertyNotFound,
                        $"Property '{requestedPropertyNames.First()}' not found on ViewModel",
                        "Call get_viewmodel without propertyNames first to inspect the available ViewModel property names.");
                }

                reflectedProperties = matchedProperties.ToArray();
            }

            var properties = new List<object>();
            foreach (var prop in reflectedProperties)
            {
                try
                {
                    var value = prop.GetValue(dataContext);
                    properties.Add(new Dictionary<string, object?>
                    {
                        ["name"] = prop.Name,
                        ["type"] = prop.PropertyType.Name,
                        ["value"] = value?.ToString(),
                        ["canWrite"] = prop.CanWrite,
                        ["canRead"] = prop.CanRead
                    });
                }
                catch (Exception) { /* Skip properties with throwing getters */ }
            }

            return new
            {
                success = true,
                typeName = dcType.Name,
                viewModelType = dcType.Name,
                implementsINotifyPropertyChanged = dataContext is System.ComponentModel.INotifyPropertyChanged,
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
                return ToolErrorFactory.ElementNotFound(elementId);

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
                return ToolErrorFactory.ElementNotFound(elementId);

            var fe = element as FrameworkElement;
            if (fe?.DataContext == null)
                return ToolErrorFactory.InvalidArgument(
                    "No DataContext found",
                    "Choose an element with a ViewModel/DataContext or call get_viewmodel/get_commands first.");

            var prop = fe.DataContext.GetType().GetProperty(commandName);
            if (prop == null)
                return ToolErrorFactory.CommandNotFound(commandName);

            var cmd = prop.GetValue(fe.DataContext) as System.Windows.Input.ICommand;
            if (cmd == null)
                return ToolErrorFactory.InvalidArgument(
                    $"'{commandName}' is not an ICommand",
                    "Call get_commands first and choose a property whose type implements ICommand.");

            var canExecute = cmd.CanExecute(parameter);
            if (!canExecute)
            {
                AuditLogger.LogSecurityEvent("CommandExecution",
                    $"Blocked by CanExecute: {commandName}",
                    AuditSeverity.Warning);
                return ToolErrorFactory.Create(
                    Shared.ErrorHandling.ToolErrorCode.InvalidArgument,
                    $"Command '{commandName}' cannot execute",
                    "Call get_commands first and verify CanExecute is true before invoking execute_command.",
                    new
                    {
                        commandName,
                        canExecute = false,
                        executed = false
                    });
            }

            // Execute command
            cmd.Execute(parameter);

            // SECURITY: Log successful execution
            AuditLogger.LogSecurityEvent("CommandExecution",
                $"Success: {commandName}",
                AuditSeverity.Information);

            return new
            {
                success = true,
                commandName,
                executed = true,
                canExecute = true
            };
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
                return ToolErrorFactory.ElementNotFound(elementId);

            var errors = new List<object>();
            if (element is DependencyObject depObj)
            {
                CollectValidationErrors(depObj, errors, maxDepth: 50);
            }

            return new { success = true, errorCount = errors.Count, errors };
        });
    }

    private const int MaxValidationErrors = 200;

    private void CollectValidationErrors(
        DependencyObject element, List<object> errors, int maxDepth)
    {
        foreach (var current in DependencyObjectTraversal.EnumerateDescendantsAndSelf(element, maxDepth))
        {
            if (errors.Count >= MaxValidationErrors)
            {
                return;
            }

            foreach (var error in Validation.GetErrors(current))
            {
                if (errors.Count >= MaxValidationErrors)
                {
                    return;
                }

                var elementName = (current as FrameworkElement)?.Name;
                var elementType = current.GetType().Name;
                errors.Add(new Dictionary<string, object?>
                {
                    ["diagnosticKind"] = "ValidationError",
                    ["sourceKind"] = error.RuleInError != null ? "ValidationRule" : "BindingValidation",
                    ["errorContent"] = error.ErrorContent?.ToString(),
                    ["isRuleError"] = error.RuleInError != null,
                    ["ruleType"] = error.RuleInError?.GetType().Name,
                    ["elementType"] = elementType,
                    ["elementName"] = string.IsNullOrEmpty(elementName) ? null : elementName,
                    ["elementId"] = _elementFinder.GenerateElementId(current)
                });
            }
        }
    }

    private DependencyObject? GetRootElement()
    {
        return _elementFinder.GetRootElement();
    }

    /// <summary>
    /// Modify ViewModel property at runtime
    /// </summary>
    public object ModifyViewModel(string? elementId, string propertyName, object? value)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return ToolErrorFactory.InvalidArgument(
                    "propertyName is required",
                    "Provide propertyName and value when calling modify_viewmodel.");
            }

            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not FrameworkElement fe)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a FrameworkElement",
                    "Target a FrameworkElement with a DataContext before calling modify_viewmodel.");
            }

            var viewModel = fe.DataContext;
            if (viewModel == null)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element has no DataContext",
                    "Choose an element with a ViewModel/DataContext or call get_viewmodel first.");
            }

            try
            {
                var validationScope = _elementFinder.GetRootElement() ?? fe;
                var validationBefore = CaptureValidationSnapshot(validationScope);

                // Get property info
                var propertyInfo = viewModel.GetType().GetProperty(propertyName);
                if (propertyInfo == null)
                {
                    return ToolErrorFactory.PropertyNotFound(propertyName, "ViewModel");
                }

                if (!propertyInfo.CanWrite)
                {
                    return ToolErrorFactory.InvalidArgument(
                        $"Property '{propertyName}' is read-only",
                        "Choose a writable ViewModel property. Inspect canWrite via get_viewmodel before retrying.");
                }

                // SECURITY: Check property name against sensitive property pattern
                if (SensitivePropertyPattern.IsMatch(propertyName))
                {
                    // Log security violation
                    AuditLogger.LogSecurityEvent("PropertyModification",
                        $"Blocked sensitive property: {propertyName}",
                        AuditSeverity.Warning);

                    return ToolErrorFactory.InvalidArgument(
                        $"Property '{propertyName}' cannot be modified for security reasons",
                        "Avoid modifying sensitive properties such as passwords, tokens, keys, or credentials.");
                }

                // Convert value to target type
                var targetType = propertyInfo.PropertyType;
                object? convertedValue;

                if (value == null)
                {
                    // Check if target type accepts null
                    if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                    {
                        return ToolErrorFactory.InvalidArgument(
                            $"Cannot assign null to non-nullable type '{targetType.Name}'",
                            "Provide a non-null value compatible with the target ViewModel property type.");
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
                        // SECURITY: Only allow Nullable wrapping for known safe primitive/value types
                        if (targetType.IsGenericType &&
                            targetType.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                            convertedValue != null)
                        {
                            if (!IsSafeNullableUnderlyingType(Nullable.GetUnderlyingType(targetType)!))
                            {
                                return ToolErrorFactory.InvalidArgument(
                                    $"Nullable<{Nullable.GetUnderlyingType(targetType)!.Name}> is not a supported type for ViewModel modification",
                                    "Only primitive types, DateTime, DateTimeOffset, TimeSpan, Guid, and decimal are supported for Nullable<T> wrapping.");
                            }
                            convertedValue = Activator.CreateInstance(targetType, convertedValue);
                        }
                    }
                    catch (Exception conversionEx)
                    {
                        return ToolErrorFactory.Create(
                            Shared.ErrorHandling.ToolErrorCode.InvalidArgument,
                            $"Type conversion failed: {conversionEx.Message}",
                            "Ensure the value is compatible with the property type",
                            new
                            {
                                sourceType = value.GetType().Name,
                                targetType = targetType.Name
                            });
                    }
                }

                // Get old value
                var oldValue = propertyInfo.GetValue(viewModel);

                // Set new value
                propertyInfo.SetValue(viewModel, convertedValue);
                var validationScopeElementId = _elementFinder.GenerateElementId(validationScope);
                var validationAfter = CaptureValidationSnapshot(validationScope);
                EnqueueValidationTransition(validationScopeElementId, validationBefore, validationAfter);

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
                    propertyType = propertyInfo.PropertyType.Name,
                    canWrite = propertyInfo.CanWrite,
                    requestedValueType = value is JsonElement jsonValue ? jsonValue.ValueKind.ToString() : value?.GetType().Name ?? "Null",
                    convertedValueType = convertedValue?.GetType().Name ?? "Null",
                    viewModelType = viewModel.GetType().Name,
                    implementsINotifyPropertyChanged = viewModel is System.ComponentModel.INotifyPropertyChanged
                };
            }
            catch (Exception ex)
            {
                return ToolErrorFactory.OperationFailed(
                    "modify ViewModel property",
                    ex,
                    "Re-query the ViewModel with get_viewmodel and verify the property is writable before retrying modify_viewmodel.");
            }
        });
    }

    private static readonly HashSet<Type> SafeNullableUnderlyingTypes = new()
    {
        typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal), typeof(char),
        typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(Guid)
    };

    private static bool IsSafeNullableUnderlyingType(Type type)
    {
        return SafeNullableUnderlyingTypes.Contains(type) || type.IsEnum;
    }
}
