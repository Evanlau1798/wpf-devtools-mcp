using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class BindingAnalyzer
{
    /// <summary>
    /// Cross-reference binding source and target types to surface deterministic mismatch diagnostics.
    /// </summary>
    public object GetBindingMismatches(string? elementId = null, bool recursive = false, bool includeFramework = false)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = ResolveElement(elementId);
            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            BindingScanBudget? budget = null;
            var mismatches = recursive
                ? CollectBindingMismatchesRecursive(element, includeFramework, out budget)
                : GetBindingMismatchesForElement(element, includeFramework);

            return new
            {
                success = true,
                mismatchCount = mismatches.Count,
                mismatches,
                truncated = budget?.Truncated ?? false,
                scanBudget = budget?.ToContract(mismatches.Count)
            };
        });
    }

    private List<object> CollectBindingMismatchesRecursive(
        DependencyObject root,
        bool includeFramework,
        out BindingScanBudget budget)
    {
        var mismatches = new List<object>();
        budget = new BindingScanBudget(
            DefaultBindingTraversalNodeLimit,
            DefaultBindingResultLimit,
            "traversal-node-limit",
            "result-limit");
        var traversal = DependencyObjectTraversal.EnumerateDescendantsAndSelfWithMetadata(
            root,
            maxDepth: 50,
            maxNodes: budget.MaxTraversalNodes);
        foreach (var element in traversal)
        {
            if (!budget.TryTakeTraversalNode())
            {
                break;
            }

            var stopDueToResultLimit = false;
            foreach (var mismatch in GetBindingMismatchesForElement(element, includeFramework))
            {
                if (budget.TryTakeResult())
                {
                    mismatches.Add(mismatch);
                    if (budget.ResultLimitReached)
                    {
                        stopDueToResultLimit = true;
                        break;
                    }
                }
            }

            if (stopDueToResultLimit)
            {
                budget.MarkResultTruncated();
                break;
            }
        }

        if (traversal.Truncated)
        {
            budget.MarkTraversalTruncated();
        }

        return mismatches;
    }

    private List<object> GetBindingMismatchesForElement(DependencyObject element, bool includeFramework)
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
                    if (!includeFramework && string.Equals(mismatch["origin"] as string, "FrameworkTemplate", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    mismatches.Add(mismatch);
                }
            }
        }

        return mismatches;
    }

    private Dictionary<string, object?>? AnalyzeBindingMismatch(
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
        var resolvedType = ResolveBoundPropertyType(sourceRoot, binding);
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

    private (bool Success, Type? Type) ResolveBoundPropertyType(object? sourceRoot, Binding binding)
    {
        if (sourceRoot == null)
        {
            return (false, null);
        }

        var resolvedBindingPath = binding.Path?.Path;
        if (string.IsNullOrWhiteSpace(resolvedBindingPath))
        {
            return (true, sourceRoot.GetType());
        }

        var currentType = sourceRoot.GetType();
        var segments = resolvedBindingPath!
            .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .ToArray();
        foreach (var segment in segments)
        {
            if (TryResolvePathParameterType(binding.Path!, segment, out var parameterType))
            {
                currentType = parameterType!;
                continue;
            }

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

    private static bool TryResolvePathParameterType(
        PropertyPath propertyPath,
        string segment,
        out Type? parameterType)
    {
        parameterType = null;
        if (segment.Length < 3
            || segment[0] != '('
            || segment[segment.Length - 1] != ')'
            || !int.TryParse(segment.Substring(1, segment.Length - 2), out var parameterIndex)
            || parameterIndex < 0
            || parameterIndex >= propertyPath.PathParameters.Count)
        {
            return false;
        }

        parameterType = propertyPath.PathParameters[parameterIndex] switch
        {
            DependencyProperty dependencyProperty => dependencyProperty.PropertyType,
            PropertyInfo propertyInfo => propertyInfo.PropertyType,
            PropertyDescriptor propertyDescriptor => propertyDescriptor.PropertyType,
            _ => null
        };
        return parameterType != null;
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

    private Dictionary<string, object?> BuildMismatchPayload(
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
            ["origin"] = ClassifyMismatchOrigin(element),
            ["diagnosis"] = diagnosis,
            ["severity"] = severity
        };
    }

    private static string ClassifyMismatchOrigin(DependencyObject element)
    {
        if (element is not FrameworkElement frameworkElement || frameworkElement.TemplatedParent == null)
        {
            return "UserCode";
        }

        if (HasFrameworkTemplateOrigin(frameworkElement))
        {
            return "FrameworkTemplate";
        }

        return "UserCode";
    }

    private static bool HasFrameworkTemplateOrigin(FrameworkElement element)
    {
        if (!IsFrameworkAssembly(element.GetType().Assembly.GetName().Name))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(element.Name))
        {
            return true;
        }

        return HasFrameworkTemplatePartName(element.Name);
    }

    private static bool HasFrameworkTemplatePartName(string elementName)
    {
        return elementName.StartsWith("PART_", StringComparison.Ordinal) ||
               elementName.StartsWith("DG_", StringComparison.Ordinal);
    }

    private static bool IsFrameworkAssembly(string? assemblyName)
    {
        return string.Equals(assemblyName, "PresentationFramework", StringComparison.Ordinal) ||
               string.Equals(assemblyName, "PresentationCore", StringComparison.Ordinal);
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
