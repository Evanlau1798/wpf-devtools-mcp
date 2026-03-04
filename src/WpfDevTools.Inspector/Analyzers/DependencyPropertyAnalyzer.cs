using System.Collections.Concurrent;
using System.Windows;
using System.ComponentModel;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF DependencyProperty values and sources
/// </summary>
public class DependencyPropertyAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;
    private static readonly ConcurrentDictionary<string, (DependencyPropertyDescriptor Descriptor, EventHandler Handler)> _watchers = new();
    private static readonly List<object> _changeLog = new();
    private static readonly object _watchLock = new object();
    private const int MaxChangeLogEntries = 10000;

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

            // Get value source
            var valueSource = DependencyPropertyHelper.GetValueSource(depObj, dp);
            var effectiveValue = depObj.GetValue(dp);

            return new
            {
                success = true,
                propertyName = propertyName,
                baseValueSource = valueSource.BaseValueSource.ToString(),
                isExpression = valueSource.IsExpression,
                isAnimated = valueSource.IsAnimated,
                isCoerced = valueSource.IsCoerced,
                isCurrent = valueSource.IsCurrent,
                effectiveValue = effectiveValue?.ToString()
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
                defaultValue = metadata.DefaultValue?.ToString(),
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
                // Convert value to correct type
                var targetType = dp.PropertyType;
                var convertedValue = ConvertValue(value, targetType);

                depObj.SetValue(dp, convertedValue);
                return new { success = true, message = $"Property '{propertyName}' set successfully" };
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
                depObj.ClearValue(dp);
                return new { success = true, message = $"Property '{propertyName}' cleared successfully" };
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
                    EventHandler handler = (sender, e) =>
                    {
                        lock (_watchLock)
                        {
                            var newValue = depObj.GetValue(dp);
                            _changeLog.Add(new
                            {
                                timestamp = DateTime.UtcNow,
                                elementId,
                                propertyName,
                                newValue = newValue?.ToString(),
                                valueType = newValue?.GetType().Name
                            });

                            // Trim oldest entries if over limit
                            if (_changeLog.Count > MaxChangeLogEntries)
                            {
                                _changeLog.RemoveRange(0, _changeLog.Count - MaxChangeLogEntries);
                            }
                        }
                    };

                    descriptor.AddValueChanged(depObj, handler);
                    _watchers[watchKey] = (descriptor, handler);
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
                var element = elementId == null
                    ? _elementFinder.GetRootElement()
                    : _elementFinder.FindById(elementId);

                if (element != null)
                {
                    watcher.Descriptor.RemoveValueChanged(element, watcher.Handler);
                }

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
        lock (_watchLock)
        {
            return new
            {
                success = true,
                changeCount = _changeLog.Count,
                changes = _changeLog.ToList()
            };
        }
    }

    /// <summary>
    /// Clear change log
    /// </summary>
    public object ClearChangeLog()
    {
        lock (_watchLock)
        {
            _changeLog.Clear();
            return new { success = true, message = "Change log cleared" };
        }
    }
}
