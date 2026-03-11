using System.ComponentModel;
using System.Windows;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class DependencyPropertyAnalyzer
{
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
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not DependencyObject depObj)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a DependencyObject",
                    "Target a WPF DependencyObject element before watching DependencyProperty changes.");
            }

            var dp = FindDependencyProperty(depObj, propertyName);
            if (dp == null)
            {
                return ToolErrorFactory.PropertyNotFound(propertyName, depObj.GetType().Name);
            }

            try
            {
                var watchKey = $"{elementId}_{propertyName}";
                if (_watchers.ContainsKey(watchKey))
                {
                    return ToolErrorFactory.InvalidArgument(
                        "Already watching this property",
                        "Reuse the existing watcher or clear it before registering the same property again.");
                }

                var descriptor = DependencyPropertyDescriptor.FromProperty(dp, depObj.GetType());
                if (descriptor != null)
                {
                    var weakElement = new WeakReference<DependencyObject>(depObj);
                    EventHandler handler = (_, _) =>
                    {
                        if (!weakElement.TryGetTarget(out var trackedElement))
                        {
                            return;
                        }

                        var newValue = trackedElement.GetValue(dp);
                        _changeLog.Enqueue(new
                        {
                            timestamp = DateTime.UtcNow,
                            elementId,
                            propertyName,
                            newValue = newValue?.ToString(),
                            valueType = newValue?.GetType().Name
                        });

                        var count = Interlocked.Increment(ref _changeLogCount);
                        while (count > MaxChangeLogEntries)
                        {
                            if (_changeLog.TryDequeue(out _))
                            {
                                count = Interlocked.Decrement(ref _changeLogCount);
                            }
                            else
                            {
                                break;
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
                return ToolErrorFactory.OperationFailed(
                    "watch property",
                    ex,
                    "Verify the property supports change notifications and that the target element is still alive before retrying watch_dp_changes.");
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
                if (watcher.ElementRef.TryGetTarget(out var element))
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

            return ToolErrorFactory.InvalidArgument(
                "Property is not being watched",
                "Register watch_dp_changes for the element/property pair before attempting to stop it.");
        });
    }

    /// <summary>
    /// Get change log for watched properties
    /// </summary>
    public object GetChangeLog() => new
    {
        success = true,
        changeCount = _changeLogCount,
        changes = _changeLog.ToArray()
    };

    /// <summary>
    /// Clear change log
    /// </summary>
    public object ClearChangeLog()
    {
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

    private static void CleanupDeadWatchers()
    {
        var deadKeys = new List<string>();

        foreach (var kvp in _watchers)
        {
            if (!kvp.Value.ElementRef.TryGetTarget(out _))
            {
                deadKeys.Add(kvp.Key);
            }
        }

        foreach (var key in deadKeys)
        {
            _watchers.TryRemove(key, out _);
        }
    }
}
