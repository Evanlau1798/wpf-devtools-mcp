using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.ComponentModel;
using System.Windows.Threading;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF DependencyProperty values and sources
///
/// DESIGN NOTE - Static Mutable State:
/// This class intentionally uses static mutable state for property change tracking.
/// Rationale:
/// 1. Property watchers should persist across analyzer instances
/// 2. Change log is global per-application for centralized monitoring
/// 3. Multiple MCP tool calls should access the same watcher registry
///
/// Thread Safety: ConcurrentDictionary and ConcurrentQueue provide thread-safe operations
/// Memory Safety: WeakReference prevents memory leaks when elements are GC'd
/// </summary>
public sealed partial class DependencyPropertyAnalyzer : DispatcherAnalyzerBase
{
    private readonly record struct WatchRegistration(
        DependencyPropertyDescriptor Descriptor,
        EventHandler Handler,
        WeakReference<DependencyObject> ElementRef,
        Dispatcher? Dispatcher);

    private readonly ElementFinder _elementFinder;
    private readonly WatchEventBuffer? _watchEventBuffer;

    // Static state for global property change tracking
    // Thread-safe via ConcurrentDictionary/ConcurrentQueue
    private static readonly ConcurrentDictionary<string, WatchRegistration> _watchers = new();
    private static readonly ConcurrentQueue<object> _changeLog = new();
    private static int _changeLogCount = 0;
    private const int MaxChangeLogEntries = 10000;
    internal static Action<DependencyPropertyDescriptor, DependencyObject, EventHandler> DetachWatcherAction { get; set; } =
        static (descriptor, element, handler) => descriptor.RemoveValueChanged(element, handler);

    // CRITICAL FIX: Timer for periodic cleanup of dead watchers
    private static readonly System.Threading.Timer _cleanupTimer;
    private const int CleanupIntervalSeconds = 30;

    static DependencyPropertyAnalyzer()
    {
        // Initialize cleanup timer (runs every 30 seconds)
        _cleanupTimer = new System.Threading.Timer(
            callback: _ => CleanupDeadWatchers(),
            state: null,
            dueTime: TimeSpan.FromSeconds(CleanupIntervalSeconds),
            period: TimeSpan.FromSeconds(CleanupIntervalSeconds));
    }

    /// <summary>
    /// Create a new DependencyPropertyAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public DependencyPropertyAnalyzer(ElementFinder elementFinder)
        : this(elementFinder, null)
    {
    }

    internal DependencyPropertyAnalyzer(
        ElementFinder elementFinder,
        WatchEventBuffer? watchEventBuffer)
    {
        _elementFinder = elementFinder;
        _watchEventBuffer = watchEventBuffer;
    }

    /// <summary>
    /// Get value source for a DependencyProperty
    /// </summary>
    public object GetValueSource(string propertyName, string? elementId = null, bool compact = false, bool settleBindings = false)
    {
        var element = elementId == null
            ? _elementFinder.GetRootElement()
            : _elementFinder.FindById(elementId);

        if (element == null)
        {
            return ToolErrorFactory.ElementNotFound(elementId);
        }

        if (element is not DependencyObject depObj)
        {
            return ToolErrorFactory.InvalidArgument(
                "Element is not a DependencyObject",
                "Choose a WPF DependencyObject target from get_visual_tree or find_elements before inspecting dependency properties.");
        }

        return InvokeSnapshotRead(depObj, settleBindings, () =>
        {
            var dp = FindDependencyProperty(depObj, propertyName);
            if (dp == null)
            {
                return ToolErrorFactory.PropertyNotFound(propertyName, depObj.GetType().Name);
            }

            var valueSource = DependencyPropertyHelper.GetValueSource(depObj, dp);
            var effectiveValue = depObj.GetValue(dp);
            var localValue = depObj.ReadLocalValue(dp);
            var hadLocalValue = localValue != DependencyProperty.UnsetValue;
            var rawBaseValueSource = valueSource.BaseValueSource.ToString();

            var baseValueSource = DependencyPropertyValueSourceNormalizer.Normalize(valueSource.BaseValueSource, hadLocalValue, valueSource.IsAnimated);
            var effectiveValueText = FormatResponseValue(effectiveValue);
            var localValueKind = GetLocalValueKind(hadLocalValue, valueSource.IsExpression);

            if (compact)
            {
                return (object)new
                {
                    success = true,
                    propertyName = propertyName,
                    baseValueSource,
                    effectiveValue = effectiveValueText
                };
            }

            return new
            {
                success = true,
                propertyName = propertyName,
                baseValueSource,
                rawBaseValueSource,
                isExpression = valueSource.IsExpression,
                isAnimated = valueSource.IsAnimated,
                isCoerced = valueSource.IsCoerced,
                isCurrent = valueSource.IsCurrent,
                currentValue = effectiveValueText,
                effectiveValue = effectiveValueText,
                hadLocalValue,
                localValueKind,
                localValue = hadLocalValue ? FormatResponseValue(localValue) : null,
                localValueType = hadLocalValue ? localValue?.GetType().Name : null
            };
        });
    }

    /// <summary>
    /// Get metadata for a DependencyProperty
    /// </summary>
    public object GetMetadata(string propertyName, string? elementId = null)
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

            if (element is not DependencyObject depObj)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a DependencyObject",
                    "Target a WPF DependencyObject element before reading DependencyProperty metadata.");
            }

            // Find DependencyProperty by name
            var dp = FindDependencyProperty(depObj, propertyName);
            if (dp == null)
            {
                return ToolErrorFactory.PropertyNotFound(propertyName, depObj.GetType().Name);
            }

            // Get metadata
            var metadata = dp.GetMetadata(depObj.GetType());

            return new
            {
                success = true,
                propertyName,
                defaultValue = FormatMetadataValue(metadata.DefaultValue),
                hasCoerceValueCallback = metadata.CoerceValueCallback != null,
                hasPropertyChangedCallback = metadata.PropertyChangedCallback != null,
                isReadOnly = dp.ReadOnly,
                ownerType = dp.OwnerType.Name,
                propertyType = dp.PropertyType.Name,
                // Framework metadata (if available)
                affectsArrange = (metadata as FrameworkPropertyMetadata)?.AffectsArrange ?? false,
                affectsMeasure = (metadata as FrameworkPropertyMetadata)?.AffectsMeasure ?? false,
                affectsRender = (metadata as FrameworkPropertyMetadata)?.AffectsRender ?? false,
                inherits = (metadata as FrameworkPropertyMetadata)?.Inherits ?? false,
                isDataBindingAllowed = (metadata as FrameworkPropertyMetadata)?.IsDataBindingAllowed ?? true
            };
        });
    }

    private static string? FormatMetadataValue(object? value) => FormatResponseValue(value);

    private static string? GetLocalValueKind(bool hadLocalValue, bool isExpression)
    {
        if (!hadLocalValue)
        {
            return null;
        }

        return isExpression
            ? "Expression"
            : "ManualOverride";
    }

    /// <summary>
    /// Set local value for a DependencyProperty
    /// </summary>
    public object SetValue(string propertyName, object value, string? elementId = null)
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

            if (element is not DependencyObject depObj)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a DependencyObject",
                    "Target a WPF DependencyObject element before setting a DependencyProperty value.");
            }

            // Find DependencyProperty by name
            var dp = FindDependencyProperty(depObj, propertyName);
            if (dp == null)
            {
                return ToolErrorFactory.PropertyNotFound(propertyName, depObj.GetType().Name);
            }

            try
            {
                var oldValue = depObj.GetValue(dp);
                var localValueBefore = depObj.ReadLocalValue(dp);
                var hadLocalValueBefore = localValueBefore != DependencyProperty.UnsetValue;
                var previousValueSource = DependencyPropertyHelper.GetValueSource(depObj, dp);
                string? expressionKind = null;
                var capturedRollbackExpression = previousValueSource.IsExpression &&
                    TryCaptureBindingExpression(depObj, dp, elementId, propertyName, out _, out expressionKind);
                // Convert value to correct type
                var targetType = dp.PropertyType;
                var convertedValue = ConvertValue(value, targetType);

                AuditLogger.LogSecurityEvent("DependencyProperty", $"Property '{propertyName}' set on element '{elementId ?? "root"}'");
                depObj.SetValue(dp, convertedValue);
                var newValue = depObj.GetValue(dp);
                var valueSource = DependencyPropertyHelper.GetValueSource(depObj, dp);

                return new
                {
                    success = true,
                    message = $"Property '{propertyName}' set successfully",
                    propertyName,
                    oldValue = FormatResponseValue(oldValue),
                    newValue = FormatResponseValue(newValue),
                    requestedValue = FormatResponseValue(value),
                    hadLocalValueBefore,
                    previousLocalValue = hadLocalValueBefore ? FormatResponseValue(localValueBefore) : null,
                    previousBaseValueSource = previousValueSource.BaseValueSource.ToString(),
                    replacedExpression = previousValueSource.IsExpression,
                    capturedRollbackExpression,
                    replacedExpressionKind = capturedRollbackExpression ? expressionKind : null,
                    baseValueSource = valueSource.BaseValueSource.ToString(),
                    valueType = newValue?.GetType().Name
                };
            }
            catch (Exception ex)
            {
                return ToolErrorFactory.OperationFailed(
                    "set property",
                    ex,
                    "Verify the property accepts local values and that the provided value is compatible with the target property type.");
            }
        });
    }

    /// <summary>
    /// Clear local value for a DependencyProperty
    /// </summary>
    public object ClearValue(string propertyName, string? elementId = null)
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

            if (element is not DependencyObject depObj)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a DependencyObject",
                    "Target a WPF DependencyObject element before clearing a DependencyProperty value.");
            }

            // Find DependencyProperty by name
            var dp = FindDependencyProperty(depObj, propertyName);
            if (dp == null)
            {
                return ToolErrorFactory.PropertyNotFound(propertyName, depObj.GetType().Name);
            }

            try
            {
                var hadLocalValue = depObj.ReadLocalValue(dp) != DependencyProperty.UnsetValue;
                var clearedValue = depObj.GetValue(dp);
                var restoredExpression = TryRestoreLatestCapturedExpression(
                    depObj,
                    dp,
                    elementId,
                    propertyName,
                    out var expressionKind,
                    out var restoredValue,
                    out var restoreError);

                if (restoreError != null)
                {
                    return ToolErrorFactory.OperationFailed(
                        "restore expression-backed property",
                        new InvalidOperationException(restoreError),
                        "Retry the mutation within the same session before calling clear_dp_value, or query get_dp_value_source to inspect the current expression state.");
                }

                if (!restoredExpression)
                {
                    depObj.ClearValue(dp);
                }

                var newValue = restoredExpression ? restoredValue : depObj.GetValue(dp);
                var valueSource = DependencyPropertyHelper.GetValueSource(depObj, dp);

                return new
                {
                    success = true,
                    message = $"Property '{propertyName}' cleared successfully",
                    propertyName,
                    hadLocalValue,
                    clearedValue = FormatResponseValue(clearedValue),
                    newValue = FormatResponseValue(newValue),
                    restoredExpression,
                    expressionKind,
                    baseValueSource = valueSource.BaseValueSource.ToString(),
                    valueType = newValue?.GetType().Name
                };
            }
            catch (Exception ex)
            {
                return ToolErrorFactory.OperationFailed(
                    "clear property",
                    ex,
                    "Verify the property supports clearing local values and still belongs to the current target element.");
            }
        });
    }
}
