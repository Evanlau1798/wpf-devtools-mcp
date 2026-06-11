using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using WpfDevTools.Inspector.Events;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Utilities;

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
            var element = ResolveElement(elementId);

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
                lock (_watchRegistrationLock)
                {
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
                        var resolvedElementId = elementId ?? _elementFinder.GenerateElementId(depObj);
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
                            _watchEventBuffer?.Enqueue(new WatchEventRecord(
                                EventType: "DpChange",
                                TimestampUtc: DateTimeOffset.UtcNow,
                                SourceKey: $"dp:{resolvedElementId}:{propertyName}",
                                ElementId: resolvedElementId,
                                PropertyName: propertyName,
                                EventName: null,
                                NewValue: newValue?.ToString(),
                                ValueType: newValue?.GetType().Name,
                                SenderType: null,
                                SenderName: null,
                                RoutingStrategy: null,
                                Handled: null,
                                OriginalSourceType: null));

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
                        var registration = new WatchRegistration(
                            descriptor,
                            handler,
                            new WeakReference<DependencyObject>(depObj),
                            depObj.Dispatcher);
                        if (!_watchers.TryAdd(watchKey, registration))
                        {
                            if (!TryDetachWatcherHandler(
                                descriptor,
                                depObj,
                                handler,
                                depObj.Dispatcher,
                                out var rollbackFailure))
                            {
                                throw new InvalidOperationException(
                                    $"Failed to rollback duplicate watcher registration '{watchKey}'.",
                                    rollbackFailure);
                            }

                            return ToolErrorFactory.InvalidArgument(
                                "Already watching this property",
                                "Reuse the existing watcher or clear it before registering the same property again.");
                        }
                    }
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

            if (_watchers.TryGetValue(watchKey, out var watcher))
            {
                if (TryDetachWatcher(watchKey, watcher, out var cleanupFailure))
                {
                    return new
                    {
                        success = true,
                        message = $"Stopped watching property '{propertyName}'",
                        propertyName,
                        elementId
                    };
                }

                return ToolErrorFactory.OperationFailed(
                    "stop watching property",
                    cleanupFailure ?? new InvalidOperationException($"Failed to detach watcher '{watchKey}'"),
                    "Retry watch cleanup after the owning dispatcher becomes available or the target element is valid again.");
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
        changeCount = _changeLog.Count,
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
    /// Clear transient watch registrations after a successful STDIO drain cycle.
    /// This must run on the owning UI dispatcher so WPF handler detachment is real,
    /// not just a registry mutation.
    /// </summary>
    public Exception? ClearTransientWatchers()
    {
        try
        {
            Exception? cleanupFailure = null;

            InvokeOnUIThread(() =>
            {
                var failedWatchKeys = new List<string>();

                foreach (var watcherEntry in _watchers.ToArray())
                {
                    if (!TryDetachWatcher(watcherEntry.Key, watcherEntry.Value, out var ex))
                    {
                        var detachFailure = ex ?? new InvalidOperationException($"Failed to detach transient watcher '{watcherEntry.Key}'.");
                        failedWatchKeys.Add(watcherEntry.Key);
                        cleanupFailure = cleanupFailure is null
                            ? new InvalidOperationException(
                                $"Failed to detach {failedWatchKeys.Count} transient watcher(s): {string.Join(", ", failedWatchKeys)}",
                                detachFailure)
                            : new AggregateException(cleanupFailure, detachFailure);
                    }
                }

                if (failedWatchKeys.Count > 0 && cleanupFailure is not InvalidOperationException)
                {
                    cleanupFailure = new InvalidOperationException(
                        $"Failed to detach {failedWatchKeys.Count} transient watcher(s): {string.Join(", ", failedWatchKeys)}",
                        cleanupFailure);
                }

                if (cleanupFailure != null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"DependencyPropertyAnalyzer: {SensitiveLogRedactor.Redact(cleanupFailure.Message)}");
                }
            });

            return cleanupFailure;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// Stop all active property watchers (cleanup on shutdown)
    /// </summary>
    public static void StopAllWatchers()
    {
        var failedWatchKeys = new List<string>();

        foreach (var kvp in _watchers.ToArray())
        {
            if (!TryDetachWatcher(kvp.Key, kvp.Value, out var cleanupFailure) && cleanupFailure != null)
            {
                failedWatchKeys.Add(kvp.Key);
                System.Diagnostics.Debug.WriteLine(
                    $"DependencyPropertyAnalyzer: Failed to cleanup watcher '{SensitiveLogRedactor.Redact(kvp.Key)}': {SensitiveLogRedactor.Redact(cleanupFailure.Message)}");
            }
        }

        _cleanupTimer?.Change(
            _watchers.IsEmpty ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(CleanupIntervalSeconds),
            _watchers.IsEmpty ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(CleanupIntervalSeconds));

        if (failedWatchKeys.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"DependencyPropertyAnalyzer: {failedWatchKeys.Count} watcher(s) remain registered for retry: {SensitiveLogRedactor.Redact(string.Join(", ", failedWatchKeys))}");
        }
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

    private static bool TryDetachWatcher(string watchKey, WatchRegistration watcher, out Exception? cleanupFailure)
    {
        if (!watcher.ElementRef.TryGetTarget(out var element))
        {
            _watchers.TryRemove(watchKey, out _);
            cleanupFailure = null;
            return true;
        }

        var dispatcher = watcher.Dispatcher ?? element.Dispatcher;
        if (TryDetachWatcherHandler(
            watcher.Descriptor,
            element,
            watcher.Handler,
            dispatcher,
            out cleanupFailure))
        {
            _watchers.TryRemove(watchKey, out _);
            return true;
        }

        return false;
    }

    private static bool TryDetachWatcherHandler(
        DependencyPropertyDescriptor descriptor,
        DependencyObject element,
        EventHandler handler,
        Dispatcher? dispatcher,
        out Exception? cleanupFailure)
    {
        if (dispatcher == null || dispatcher.HasShutdownFinished || dispatcher.HasShutdownStarted)
        {
            cleanupFailure = null;
            return true;
        }

        try
        {
            if (dispatcher.CheckAccess())
            {
                DetachWatcherAction(descriptor, element, handler);
            }
            else
            {
                dispatcher.Invoke(
                    () => DetachWatcherAction(descriptor, element, handler),
                    DispatcherPriority.Normal,
                    CancellationToken.None,
                    InspectorConfig.UIThreadTimeout);
            }

            cleanupFailure = null;
            return true;
        }
        catch (Exception ex)
        {
            cleanupFailure = ex;
            return false;
        }
    }
}
