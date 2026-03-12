using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    public object GetBindingMismatches(string? elementId = null, bool recursive = false)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = ResolveElement(elementId);
            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            var mismatches = recursive
                ? CollectBindingMismatchesRecursive(element)
                : GetBindingMismatchesForElement(element);

            return new
            {
                success = true,
                mismatchCount = mismatches.Count,
                mismatches
            };
        });
    }

    private List<object> CollectBindingMismatchesRecursive(DependencyObject root)
    {
        var mismatches = new List<object>();
        var visited = new HashSet<DependencyObject>();
        CollectBindingMismatchesRecursiveCore(root, visited, mismatches);
        return mismatches;
    }

    private void CollectBindingMismatchesRecursiveCore(
        DependencyObject element,
        HashSet<DependencyObject> visited,
        List<object> mismatches)
    {
        if (!visited.Add(element))
        {
            return;
        }

        mismatches.AddRange(GetBindingMismatchesForElement(element));

        foreach (var child in DependencyObjectTraversal.EnumerateChildren(element))
        {
            CollectBindingMismatchesRecursiveCore(child, visited, mismatches);
        }
    }

    private List<object> GetBindingMismatchesForElement(DependencyObject element)
    {
        var mismatches = new List<object>();
        var enumerator = element.GetLocalValueEnumerator();

        while (enumerator.MoveNext())
        {
            var entry = enumerator.Current;
            var dp = entry.Property;
            if (dp == null)
            {
                continue;
            }

            if (BindingOperations.GetBindingExpressionBase(element, dp) is BindingExpression bindingExpression)
            {
                var mismatch = AnalyzeBindingMismatch(element, dp, bindingExpression);
                if (mismatch != null)
                {
                    mismatches.Add(mismatch);
                }
            }
        }

        return mismatches;
    }

    private object? AnalyzeBindingMismatch(
        DependencyObject element,
        DependencyProperty property,
        BindingExpression bindingExpression)
    {
        var binding = bindingExpression.ParentBinding;
        if (binding == null)
        {
            return null;
        }

        var bindingPath = binding.Path?.Path;
        var sourceRoot = ResolveBindingSourceRoot(element, binding, bindingExpression);
        var resolvedType = ResolveBoundPropertyType(sourceRoot, bindingPath);
        var targetType = property.PropertyType;
        var converterName = binding.Converter?.GetType().Name;

        if (!resolvedType.Success)
        {
            return BuildMismatchPayload(
                element,
                property,
                bindingPath,
                targetType,
                null,
                converterName,
                "PathMismatch",
                "Warning");
        }

        var sourceType = resolvedType.Type;
        if (sourceType == null)
        {
            return null;
        }

        var (diagnosis, severity) = ClassifyMismatch(sourceType, targetType, converterName != null);
        if (diagnosis == "Compatible")
        {
            return null;
        }

        return BuildMismatchPayload(
            element,
            property,
            bindingPath,
            targetType,
            sourceType,
            converterName,
            diagnosis,
            severity);
    }

    private (bool Success, Type? Type) ResolveBoundPropertyType(object? sourceRoot, string? bindingPath)
    {
        if (sourceRoot == null)
        {
            return (false, null);
        }

        if (string.IsNullOrWhiteSpace(bindingPath))
        {
            return (true, sourceRoot.GetType());
        }

        var currentType = sourceRoot.GetType();
        var segments = bindingPath
            .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .ToArray();
        foreach (var segment in segments)
        {
            if (segment.Contains('[') || segment.Contains('('))
            {
                return (false, null);
            }

            var property = currentType.GetProperty(segment, BindingFlags.Instance | BindingFlags.Public);
            if (property == null)
            {
                return (false, null);
            }

            currentType = property.PropertyType;
        }

        return (true, currentType);
    }

    private object? ResolveBindingSourceRoot(
        DependencyObject element,
        Binding binding,
        BindingExpression bindingExpression)
    {
        if (binding.Source != null)
        {
            return binding.Source;
        }

        if (binding.RelativeSource?.Mode == RelativeSourceMode.Self)
        {
            return element;
        }

        if (element is FrameworkElement frameworkElement && !string.IsNullOrWhiteSpace(binding.ElementName))
        {
            var namedElement = FindElementByName(frameworkElement, binding.ElementName);
            if (namedElement != null)
            {
                return namedElement;
            }
        }

        return bindingExpression.DataItem;
    }

    private DependencyObject? FindElementByName(FrameworkElement origin, string elementName)
    {
        var root = origin;
        while (true)
        {
            var parent = LogicalTreeHelper.GetParent(root) as FrameworkElement ??
                         VisualTreeHelper.GetParent(root) as FrameworkElement;
            if (parent == null)
            {
                break;
            }

            root = parent;
        }

        return DependencyObjectTraversal
            .EnumerateDescendantsAndSelf(root)
            .OfType<FrameworkElement>()
            .FirstOrDefault(item => string.Equals(item.Name, elementName, StringComparison.Ordinal));
    }

    private static bool IsTypeCompatible(Type sourceType, Type targetType)
    {
        if (targetType.IsAssignableFrom(sourceType))
        {
            return true;
        }

        var sourceUnderlying = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
        var targetUnderlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (targetUnderlying.IsAssignableFrom(sourceUnderlying))
        {
            return true;
        }

        return CanTypeDescriptorConvert(sourceUnderlying, targetUnderlying);
    }

    private static bool CanTypeDescriptorConvert(Type sourceType, Type targetType)
    {
        var sourceConverter = TypeDescriptor.GetConverter(sourceType);
        if (sourceConverter.CanConvertTo(targetType))
        {
            return true;
        }

        var targetConverter = TypeDescriptor.GetConverter(targetType);
        return targetConverter.CanConvertFrom(sourceType);
    }

    private static (string Diagnosis, string Severity) ClassifyMismatch(Type sourceType, Type targetType, bool hasConverter)
    {
        if (IsNullableMismatch(sourceType, targetType))
        {
            return ("NullabilityMismatch", "Warning");
        }

        if (IsTypeCompatible(sourceType, targetType))
        {
            return ("Compatible", "Info");
        }

        return hasConverter
            ? ("TypeMismatchWithConverter", "Info")
            : ("TypeMismatch", "Warning");
    }

    private static bool IsNullableMismatch(Type sourceType, Type targetType)
    {
        var sourceUnderlying = Nullable.GetUnderlyingType(sourceType);
        if (sourceUnderlying == null)
        {
            return false;
        }

        return Nullable.GetUnderlyingType(targetType) == null &&
               !targetType.IsAssignableFrom(sourceType) &&
               targetType == sourceUnderlying;
    }

    private object BuildMismatchPayload(
        DependencyObject element,
        DependencyProperty property,
        string? bindingPath,
        Type targetType,
        Type? sourceType,
        string? converterName,
        string diagnosis,
        string severity)
    {
        return new Dictionary<string, object?>
        {
            ["elementId"] = _elementFinder.GenerateElementId(element),
            ["elementType"] = element.GetType().Name,
            ["elementName"] = (element as FrameworkElement)?.Name,
            ["propertyName"] = property.Name,
            ["bindingPath"] = bindingPath,
            ["targetType"] = FormatTypeName(targetType),
            ["sourceType"] = sourceType == null ? null : FormatTypeName(sourceType),
            ["converter"] = converterName,
            ["diagnosis"] = diagnosis,
            ["severity"] = severity
        };
    }

    private static string FormatTypeName(Type type)
    {
        var nullableUnderlying = Nullable.GetUnderlyingType(type);
        if (nullableUnderlying != null)
        {
            return $"Nullable<{nullableUnderlying.Name}>";
        }

        return type.Name;
    }
}
