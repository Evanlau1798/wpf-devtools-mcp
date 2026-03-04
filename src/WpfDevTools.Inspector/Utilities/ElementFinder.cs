using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace WpfDevTools.Inspector.Utilities;

public class ElementFinder
{
    private static int _nextId = 0;
    private readonly ConcurrentDictionary<DependencyObject, string> _objectToIdCache = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<string, WeakReference<DependencyObject>> _elementCache = new();

    public DependencyObject? GetRootElement()
    {
        return Application.Current?.MainWindow;
    }

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

        return elementId;
    }

    public DependencyObject? FindById(string? elementId, DependencyObject? root = null)
    {
        if (string.IsNullOrEmpty(elementId))
        {
            return GetRootElement();
        }

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
}

/// <summary>
/// Comparer that uses reference equality for DependencyObject keys
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
