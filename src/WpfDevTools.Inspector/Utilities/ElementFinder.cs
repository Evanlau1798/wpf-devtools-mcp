using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace WpfDevTools.Inspector.Utilities;

/// <summary>
/// Utility for finding and tracking WPF elements by ID
/// </summary>
public class ElementFinder : IDisposable
{
    // Static to ensure unique IDs across all ElementFinder instances.
    // Multiple analyzers may share the same instance, but if separate instances
    // are created (e.g., in tests), static guarantees no ID collisions.
    private static int _nextId = 0;
    private readonly ConcurrentDictionary<DependencyObject, string> _objectToIdCache = new(ReferenceEqualityComparer.Instance);
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
    /// Get the root element of the WPF application
    /// </summary>
    /// <returns>Root DependencyObject (typically MainWindow), or null if not available</returns>
    public DependencyObject? GetRootElement()
    {
        return Application.Current?.MainWindow;
    }

    /// <summary>
    /// Generate a unique ID for a WPF element
    /// </summary>
    /// <param name="element">Element to generate ID for</param>
    /// <returns>Unique element ID string</returns>
    public string GenerateElementId(DependencyObject element)
    {
        var elementId = _objectToIdCache.GetOrAdd(element, e =>
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
    /// Remove dead WeakReferences from both caches to prevent memory leaks.
    /// _objectToIdCache holds strong references as keys, so GC'd elements must
    /// be removed from both caches to allow proper garbage collection.
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

        // Also clean _objectToIdCache: remove entries whose IDs are no longer in _elementCache
        var deadObjects = new List<DependencyObject>();
        foreach (var kvp in _objectToIdCache)
        {
            if (!_elementCache.ContainsKey(kvp.Value))
            {
                deadObjects.Add(kvp.Key);
            }
        }

        foreach (var obj in deadObjects)
        {
            _objectToIdCache.TryRemove(obj, out _);
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

        // Fall back to visual tree search
        var searchRoot = root ?? GetRootElement();
        if (searchRoot == null)
        {
            return null;
        }

        return SearchVisualTree(searchRoot, elementId!);
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

/// <summary>
/// Comparer that uses reference equality for DependencyObject keys.
/// A custom implementation is needed because the BCL's System.Collections.Generic.ReferenceEqualityComparer
/// (.NET 5+) implements IEqualityComparer{object}, which cannot be used as IEqualityComparer{DependencyObject}
/// due to IEqualityComparer{T} being contravariant (in T). ConcurrentDictionary{DependencyObject, string}
/// requires IEqualityComparer{DependencyObject} specifically.
/// </summary>
internal sealed class ReferenceEqualityComparer : IEqualityComparer<DependencyObject>
{
    public static readonly ReferenceEqualityComparer Instance = new();

    private ReferenceEqualityComparer() { }

    public bool Equals(DependencyObject? x, DependencyObject? y)
    {
        return ReferenceEquals(x, y);
    }

    public int GetHashCode(DependencyObject obj)
    {
        return RuntimeHelpers.GetHashCode(obj);
    }
}
