using System.Collections.Concurrent;
using System.Windows;

namespace WpfDevTools.Inspector.Utilities;

public class ElementFinder
{
    private readonly ConcurrentDictionary<int, string> _elementIdCache = new();
    private readonly ConcurrentDictionary<string, WeakReference<DependencyObject>> _elementCache = new();

    public DependencyObject? GetRootElement()
    {
        return Application.Current?.MainWindow;
    }

    public string GenerateElementId(DependencyObject element)
    {
        var hashCode = element.GetHashCode();

        var elementId = _elementIdCache.GetOrAdd(hashCode, _ =>
        {
            var typeName = element.GetType().Name;
            return $"{typeName}_{hashCode}";
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

        // TODO: Implement visual tree search logic
        // For now, return root
        return root ?? GetRootElement();
    }
}
