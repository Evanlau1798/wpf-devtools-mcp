using System.Collections.Concurrent;
using System.Reflection;
using System.Windows;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    private static readonly ConcurrentDictionary<Type, DependencyProperty[]> CandidateDependencyPropertiesCache = new();

    private static IReadOnlyList<DependencyProperty> GetCandidateDependencyProperties(DependencyObject element)
    {
        var seen = new HashSet<DependencyProperty>();
        var properties = new List<DependencyProperty>();

        var localValueEnumerator = element.GetLocalValueEnumerator();
        while (localValueEnumerator.MoveNext())
        {
            var property = localValueEnumerator.Current.Property;
            if (property != null && seen.Add(property))
            {
                properties.Add(property);
            }
        }

        foreach (var property in CandidateDependencyPropertiesCache.GetOrAdd(element.GetType(), EnumerateDependencyProperties))
        {
            if (seen.Add(property))
            {
                properties.Add(property);
            }
        }

        return properties;
    }

    private static DependencyProperty[] EnumerateDependencyProperties(Type elementType)
    {
        var properties = new List<DependencyProperty>();
        var seen = new HashSet<DependencyProperty>();

        for (var current = elementType; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (field.FieldType != typeof(DependencyProperty))
                {
                    continue;
                }

                if (field.GetValue(null) is DependencyProperty property && seen.Add(property))
                {
                    properties.Add(property);
                }
            }
        }

        return properties.ToArray();
    }
}
