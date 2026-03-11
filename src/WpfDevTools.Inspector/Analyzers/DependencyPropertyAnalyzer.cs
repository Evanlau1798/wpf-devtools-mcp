using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.ComponentModel;
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
public sealed class DependencyPropertyAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    // Static state for global property change tracking
    // Thread-safe via ConcurrentDictionary/ConcurrentQueue
    private static readonly ConcurrentDictionary<string, (DependencyPropertyDescriptor Descriptor, EventHandler Handler, WeakReference<DependencyObject> ElementRef)> _watchers = new();
    private static readonly ConcurrentQueue<object> _changeLog = new();
    private static int _changeLogCount = 0;
    private const int MaxChangeLogEntries = 10000;

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
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Get value source for a DependencyProperty
    /// </summary>
    public object GetValueSource(string propertyName, string? elementId = null)
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
                    "Choose a WPF DependencyObject target from get_visual_tree or find_elements before inspecting dependency properties.");
            }

            // Find DependencyProperty by name
            var dp = FindDependencyProperty(depObj, propertyName);
            if (dp == null)
            {
                return ToolErrorFactory.PropertyNotFound(propertyName, depObj.GetType().Name);
            }

            // Get value source
            var valueSource = DependencyPropertyHelper.GetValueSource(depObj, dp);
            var effectiveValue = depObj.GetValue(dp);
            var localValue = depObj.ReadLocalValue(dp);
            var hadLocalValue = localValue != DependencyProperty.UnsetValue;
            var rawBaseValueSource = valueSource.BaseValueSource.ToString();

            return new
            {
                success = true,
                propertyName = propertyName,
                baseValueSource = DependencyPropertyValueSourceNormalizer.Normalize(valueSource.BaseValueSource, hadLocalValue, valueSource.IsAnimated),
                rawBaseValueSource,
                isExpression = valueSource.IsExpression,
                isAnimated = valueSource.IsAnimated,
                isCoerced = valueSource.IsCoerced,
                isCurrent = valueSource.IsCurrent,
                currentValue = FormatResponseValue(effectiveValue),
                effectiveValue = FormatResponseValue(effectiveValue),
                hadLocalValue,
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
                return new { success = false, error = "Element not found" };
            }

            if (element is not DependencyObject depObj)
            {
                return new { success = false, error = "Element is not a DependencyObject" };
            }

            // Find DependencyProperty by name
            var dp = FindDependencyProperty(depObj, propertyName);
            if (dp == null)
            {
                return new { success = false, error = $"DependencyProperty '{propertyName}' not found" };
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
                return new { success = false, error = "Element not found" };
            }

            if (element is not DependencyObject depObj)
            {
                return new { success = false, error = "Element is not a DependencyObject" };
            }

            // Find DependencyProperty by name
            var dp = FindDependencyProperty(depObj, propertyName);
            if (dp == null)
            {
                return new { success = false, error = $"DependencyProperty '{propertyName}' not found" };
            }

            try
            {
                var oldValue = depObj.GetValue(dp);
                var localValueBefore = depObj.ReadLocalValue(dp);
                var hadLocalValueBefore = localValueBefore != DependencyProperty.UnsetValue;
                var previousValueSource = DependencyPropertyHelper.GetValueSource(depObj, dp);
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
                    baseValueSource = valueSource.BaseValueSource.ToString(),
                    valueType = newValue?.GetType().Name
                };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Failed to set property: {ex.Message}" };
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
                return new { success = false, error = "Element not found" };
            }

            if (element is not DependencyObject depObj)
            {
                return new { success = false, error = "Element is not a DependencyObject" };
            }

            // Find DependencyProperty by name
            var dp = FindDependencyProperty(depObj, propertyName);
            if (dp == null)
            {
                return new { success = false, error = $"DependencyProperty '{propertyName}' not found" };
            }

            try
            {
                var hadLocalValue = depObj.ReadLocalValue(dp) != DependencyProperty.UnsetValue;
                var clearedValue = depObj.GetValue(dp);
                depObj.ClearValue(dp);
                var newValue = depObj.GetValue(dp);
                var valueSource = DependencyPropertyHelper.GetValueSource(depObj, dp);

                return new
                {
                    success = true,
                    message = $"Property '{propertyName}' cleared successfully",
                    propertyName,
                    hadLocalValue,
                    clearedValue = FormatResponseValue(clearedValue),
                    newValue = FormatResponseValue(newValue),
                    baseValueSource = valueSource.BaseValueSource.ToString(),
                    valueType = newValue?.GetType().Name
                };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Failed to clear property: {ex.Message}" };
            }
        });
    }

    /// <summary>
    /// Start watching DependencyProperty changes
    /// </summary>
    public object WatchChanges(string propertyName, string? elementId = null)
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

            if (element is not DependencyObject depObj)
            {
                return new { success = false, error = "Element is not a DependencyObject" };
            }

            // Find DependencyProperty by name
            var dp = FindDependencyProperty(depObj, propertyName);
            if (dp == null)
            {
                return new { success = false, error = $"DependencyProperty '{propertyName}' not found" };
            }

            try
            {
                var watchKey = $"{elementId}_{propertyName}";

                // Check if already watching (ConcurrentDictionary is thread-safe for reads)
                if (_watchers.ContainsKey(watchKey))
                {
                    return new { success = false, error = "Already watching this property" };
                }

                // Create descriptor and add handler (must be on UI thread)
                var descriptor = DependencyPropertyDescriptor.FromProperty(dp, depObj.GetType());
                if (descriptor != null)
                {
                    // SECURITY: Use WeakReference to prevent memory leak
                    // The closure must NOT capture depObj directly, as that creates
                    // a strong reference preventing GC of the element
                    var weakElement = new WeakReference<DependencyObject>(depObj);
                    EventHandler handler = (sender, e) =>
                    {
                        if (!weakElement.TryGetTarget(out var element))
                            return;
                        var newValue = element.GetValue(dp);
                        _changeLog.Enqueue(new
                        {
                            timestamp = DateTime.UtcNow,
                            elementId,
                            propertyName,
                            newValue = newValue?.ToString(),
                            valueType = newValue?.GetType().Name
                        });

                        // Increment count and trim oldest entries if over limit
                        var count = Interlocked.Increment(ref _changeLogCount);
                        while (count > MaxChangeLogEntries)
                        {
                            if (_changeLog.TryDequeue(out _))
                            {
                                count = Interlocked.Decrement(ref _changeLogCount);
                            }
                            else
                            {
                                break; // Queue empty, exit
                            }
                        }
                    };

                    descriptor.AddValueChanged(depObj, handler);
                    _watchers[watchKey] = (descriptor, handler, new WeakReference<DependencyObject>(depObj));
                }

                return new
                {
                    success = true,
                    message = $"Started watching property '{propertyName}'",
                    propertyName,
                    elementId
                };
            }
            catch (Exception ex)
            {
                return new { success = false, error = $"Failed to watch property: {ex.Message}" };
            }
        });
    }

    /// <summary>
    /// Stop watching DependencyProperty changes
    /// </summary>
    public object UnwatchChanges(string propertyName, string? elementId = null)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var watchKey = $"{elementId}_{propertyName}";

            if (_watchers.TryRemove(watchKey, out var watcher))
            {
                // Try to get the element from weak reference
                if (watcher.ElementRef.TryGetTarget(out var element))
                {
                    // Element is still alive, remove the event handler
                    watcher.Descriptor.RemoveValueChanged(element, watcher.Handler);
                }
                // If element is GC'd, the handler is already cleaned up by GC
                // No need for fallback lookup

                return new
                {
                    success = true,
                    message = $"Stopped watching property '{propertyName}'",
                    propertyName,
                    elementId
                };
            }

            return new { success = false, error = "Property is not being watched" };
        });
    }

    /// <summary>
    /// Get change log for watched properties
    /// </summary>
    public object GetChangeLog()
    {
        return new
        {
            success = true,
            changeCount = _changeLogCount,
            changes = _changeLog.ToArray()
        };
    }

    /// <summary>
    /// Clear change log
    /// </summary>
    public object ClearChangeLog()
    {
        // Clear queue by dequeuing all items
        while (_changeLog.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _changeLogCount, 0);
        return new { success = true, message = "Change log cleared" };
    }

    /// <summary>
    /// Stop all active property watchers (cleanup on shutdown)
    /// </summary>
    public static void StopAllWatchers()
    {
        foreach (var kvp in _watchers)
        {
            try
            {
                if (kvp.Value.ElementRef.TryGetTarget(out var element))
                {
                    kvp.Value.Descriptor.RemoveValueChanged(element, kvp.Value.Handler);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DependencyPropertyAnalyzer: Failed to cleanup watcher: {ex.Message}");
            }
        }
        _watchers.Clear();
        // Stop timer but don't dispose - allow restart via ResetMonitoring
        _cleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Reset monitoring state and restart cleanup timer.
    /// Call after StopAllWatchers when re-initializing.
    /// </summary>
    public static void ResetMonitoring()
    {
        while (_changeLog.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _changeLogCount, 0);
        _cleanupTimer?.Change(
            TimeSpan.FromSeconds(CleanupIntervalSeconds),
            TimeSpan.FromSeconds(CleanupIntervalSeconds));
    }

    /// <summary>
    /// CRITICAL FIX: Clean up watchers for garbage-collected elements
    /// This prevents dead watchers from accumulating over time
    /// </summary>
    private static void CleanupDeadWatchers()
    {
        var deadKeys = new List<string>();

        foreach (var kvp in _watchers)
        {
            // Check if element is still alive
            if (!kvp.Value.ElementRef.TryGetTarget(out _))
            {
                deadKeys.Add(kvp.Key);
            }
        }

        // Remove dead watchers
        foreach (var key in deadKeys)
        {
            _watchers.TryRemove(key, out _);
        }
    }
}
