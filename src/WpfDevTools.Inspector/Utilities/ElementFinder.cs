using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WpfDevTools.Shared.Configuration;

namespace WpfDevTools.Inspector.Utilities;

/// <summary>
/// Utility for finding and tracking WPF elements by ID
/// </summary>
public sealed class ElementFinder : IDisposable
{
    // Static to ensure unique IDs across all ElementFinder instances.
    // Multiple analyzers may share the same instance, but if separate instances
    // are created (e.g., in tests), static guarantees no ID collisions.
    private static int _nextId = 0;
    private readonly ConditionalWeakTable<DependencyObject, string> _objectToIdCache = new();
    private readonly ConcurrentDictionary<string, WeakReference<DependencyObject>> _elementCache = new();
    private readonly System.Threading.Timer _cleanupTimer;
    private const int CleanupIntervalSeconds = 30;

    /// <summary>
    /// Create a new ElementFinder instance with timer-based cleanup
    /// </summary>
    public ElementFinder()
    {
        // CRITICAL FIX: Use timer-based cleanup instead of count-based
        // This prevents GC pressure spikes in large UIs with rapid element creation
        _cleanupTimer = new System.Threading.Timer(
            callback: _ => CleanupDeadReferences(),
            state: null,
            dueTime: TimeSpan.FromSeconds(CleanupIntervalSeconds),
            period: TimeSpan.FromSeconds(CleanupIntervalSeconds));
    }

    /// <summary>
    /// Get the root element of the WPF application (defaults to MainWindow)
    /// </summary>
    /// <returns>Root DependencyObject (typically MainWindow), or null if not available</returns>
    public DependencyObject? GetRootElement()
    {
        return GetRootElement(windowIndex: null);
    }

    /// <summary>
    /// Get the root element for a specific window by index.
    /// Index 0 or null returns MainWindow.
    /// </summary>
    /// <param name="windowIndex">Zero-based window index, or null for MainWindow</param>
    /// <returns>Root DependencyObject (Window), or null if not available or out of range</returns>
    public DependencyObject? GetRootElement(int? windowIndex)
    {
        var application = Application.Current;
        if (application == null)
        {
            return null;
        }

        if (windowIndex is < 0)
        {
            return null;
        }

        return InvokeOnDispatcher(application.Dispatcher, () =>
        {
            if (windowIndex == null)
            {
                return application.MainWindow;
            }

            var windows = application.Windows;
            if (windowIndex.Value >= windows.Count)
            {
                return null;
            }

            return windows[windowIndex.Value];
        });
    }

    /// <summary>
    /// Enumerate all open windows in the WPF application
    /// </summary>
    /// <returns>List of WindowInfo for each open window</returns>
    public IReadOnlyList<WindowInfo> GetWindows()
    {
        var application = Application.Current;
        if (application == null)
        {
            return Array.Empty<WindowInfo>();
        }

        return InvokeOnDispatcher(application.Dispatcher, () =>
        {
            var windows = application.Windows;
            var result = new List<WindowInfo>(windows.Count);

            for (var i = 0; i < windows.Count; i++)
            {
                var window = windows[i];
                result.Add(new WindowInfo
                {
                    Index = i,
                    Title = window.Title ?? string.Empty,
                    Type = window.GetType().Name,
                    IsActive = window.IsActive,
                    IsVisible = window.IsVisible,
                    IsMainWindow = ReferenceEquals(window, application.MainWindow),
                    ElementId = GenerateElementId(window)
                });
            }

            return result;
        });
    }

    /// <summary>
    /// Generate a unique ID for a WPF element
    /// </summary>
    /// <param name="element">Element to generate ID for</param>
    /// <returns>Unique element ID string</returns>
    public string GenerateElementId(DependencyObject element)
    {
        var elementId = _objectToIdCache.GetValue(element, e =>
        {
            var id = Interlocked.Increment(ref _nextId);
            var typeName = e.GetType().Name;
            return $"{typeName}_{id}";
        });

        // Cache the element with WeakReference
        _elementCache[elementId] = new WeakReference<DependencyObject>(element);

        // CRITICAL FIX: Removed count-based cleanup trigger
        // Cleanup now runs on a timer (every 30 seconds) to prevent GC pressure spikes

        return elementId;
    }

    /// <summary>
    /// Remove dead WeakReferences from _elementCache to prevent memory leaks.
    /// _objectToIdCache uses ConditionalWeakTable which automatically releases
    /// entries when keys are garbage collected - no manual cleanup needed.
    /// </summary>
    public void CleanupDeadReferences()
    {
        var deadKeys = new List<string>();

        foreach (var kvp in _elementCache)
        {
            if (!kvp.Value.TryGetTarget(out _))
            {
                deadKeys.Add(kvp.Key);
            }
        }

        foreach (var key in deadKeys)
        {
            _elementCache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Find element by ID in the Visual Tree
    /// </summary>
    /// <param name="elementId">Element ID to search for. If null or empty, returns root element.</param>
    /// <param name="root">Root element to start search from. If null, uses application root.</param>
    /// <returns>Found DependencyObject, or null if not found</returns>
    public DependencyObject? FindById(string? elementId, DependencyObject? root = null)
    {
        if (string.IsNullOrEmpty(elementId))
        {
            return GetRootElement();
        }

        if (elementId!.Length > 256)
            return null;

        // Try to find in cache first
        if (_elementCache.TryGetValue(elementId!, out var weakRef))
        {
            if (weakRef.TryGetTarget(out var element))
            {
                return element;
            }
            else
            {
                // Element was garbage collected, remove from cache
                _elementCache.TryRemove(elementId!, out _);
            }
        }

        // Fall back to visual tree search across all windows
        var searchRoot = root ?? GetRootElement();
        if (searchRoot == null)
        {
            return null;
        }

        return InvokeOnDispatcher(searchRoot.Dispatcher, () =>
        {
            // Search the provided root first
            var found = SearchVisualTree(searchRoot, elementId!);
            if (found != null)
            {
                return found;
            }

            // If not found, search other windows (only when no explicit root was provided)
            if (root != null)
            {
                return null;
            }

            var application = Application.Current;
            if (application == null)
            {
                return null;
            }

            var windows = application.Windows;
            for (var i = 0; i < windows.Count; i++)
            {
                var window = windows[i];
                if (ReferenceEquals(window, searchRoot))
                {
                    continue;
                }

                found = SearchVisualTree(window, elementId!);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        });
    }

    internal static T InvokeOnDispatcher<T>(Dispatcher? dispatcher, Func<T> action, TimeSpan? timeout = null)
    {
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            return action();
        }

        var actualTimeout = timeout ?? InspectorConfig.UIThreadTimeout;
        return dispatcher.Invoke(
            action,
            DispatcherPriority.Normal,
            CancellationToken.None,
            actualTimeout);
    }

    private DependencyObject? SearchVisualTree(DependencyObject element, string targetId)
    {
        // Check if this element matches the target ID
        if (_objectToIdCache.TryGetValue(element, out var id) && id == targetId)
        {
            // Re-cache the weak reference
            _elementCache[targetId] = new WeakReference<DependencyObject>(element);
            return element;
        }

        // Recursively search children
        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            var found = SearchVisualTree(child, targetId);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Dispose resources (cleanup timer)
    /// </summary>
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}
