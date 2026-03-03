using System.Collections.Concurrent;
using System.Windows;

namespace WpfDevTools.Inspector.Utilities;

public class ElementFinder
{
    private readonly ConcurrentDictionary<int, string> _elementIdCache = new();

    public DependencyObject? GetRootElement()
    {
        return Application.Current?.MainWindow;
    }

    public string GenerateElementId(DependencyObject element)
    {
        var hashCode = element.GetHashCode();

        return _elementIdCache.GetOrAdd(hashCode, _ =>
        {
            var typeName = element.GetType().Name;
            return $"{typeName}_{hashCode}";
        });
    }

    public DependencyObject? FindById(string? elementId, DependencyObject? root = null)
    {
        if (string.IsNullOrEmpty(elementId))
        {
            return GetRootElement();
        }

        // TODO: Implement actual search logic
        // For now, return root
        return root ?? GetRootElement();
    }
}
