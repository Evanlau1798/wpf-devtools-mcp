using System.Windows;
using System.Windows.Data;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class DependencyPropertyAnalyzer
{
    private static BindingBase? CloneBindingBase(BindingBase bindingBase) => bindingBase switch
    {
        Binding binding => CloneBinding(binding),
        MultiBinding multiBinding => CloneMultiBinding(multiBinding),
        PriorityBinding priorityBinding => ClonePriorityBinding(priorityBinding),
        _ => null
    };

    private static Binding CloneBinding(Binding source)
    {
        var clone = new Binding
        {
            BindingGroupName = source.BindingGroupName,
            BindsDirectlyToSource = source.BindsDirectlyToSource,
            Converter = source.Converter,
            ConverterCulture = source.ConverterCulture,
            ConverterParameter = source.ConverterParameter,
            Delay = source.Delay,
            FallbackValue = source.FallbackValue,
            IsAsync = source.IsAsync,
            Mode = source.Mode,
            NotifyOnSourceUpdated = source.NotifyOnSourceUpdated,
            NotifyOnTargetUpdated = source.NotifyOnTargetUpdated,
            NotifyOnValidationError = source.NotifyOnValidationError,
            Path = source.Path,
            StringFormat = source.StringFormat,
            TargetNullValue = source.TargetNullValue,
            UpdateSourceTrigger = source.UpdateSourceTrigger,
            ValidatesOnDataErrors = source.ValidatesOnDataErrors,
            ValidatesOnExceptions = source.ValidatesOnExceptions,
            ValidatesOnNotifyDataErrors = source.ValidatesOnNotifyDataErrors,
            XPath = source.XPath
        };

        if (!string.IsNullOrWhiteSpace(source.ElementName))
        {
            clone.ElementName = source.ElementName;
        }
        else if (source.RelativeSource != null)
        {
            clone.RelativeSource = source.RelativeSource;
        }
        else if (source.Source != null && !ReferenceEquals(source.Source, DependencyProperty.UnsetValue))
        {
            clone.Source = source.Source;
        }

        foreach (var rule in source.ValidationRules)
        {
            clone.ValidationRules.Add(rule);
        }

        return clone;
    }

    private static MultiBinding CloneMultiBinding(MultiBinding source)
    {
        var clone = new MultiBinding
        {
            BindingGroupName = source.BindingGroupName,
            Converter = source.Converter,
            ConverterCulture = source.ConverterCulture,
            ConverterParameter = source.ConverterParameter,
            Delay = source.Delay,
            FallbackValue = source.FallbackValue,
            Mode = source.Mode,
            NotifyOnSourceUpdated = source.NotifyOnSourceUpdated,
            NotifyOnTargetUpdated = source.NotifyOnTargetUpdated,
            NotifyOnValidationError = source.NotifyOnValidationError,
            StringFormat = source.StringFormat,
            TargetNullValue = source.TargetNullValue,
            UpdateSourceTrigger = source.UpdateSourceTrigger
        };

        foreach (var child in source.Bindings)
        {
            if (child is BindingBase childBinding && CloneBindingBase(childBinding) is { } clonedChild)
            {
                clone.Bindings.Add(clonedChild);
            }
        }

        foreach (var rule in source.ValidationRules)
        {
            clone.ValidationRules.Add(rule);
        }

        return clone;
    }

    private static PriorityBinding ClonePriorityBinding(PriorityBinding source)
    {
        var clone = new PriorityBinding
        {
            BindingGroupName = source.BindingGroupName,
            Delay = source.Delay,
            FallbackValue = source.FallbackValue,
            StringFormat = source.StringFormat,
            TargetNullValue = source.TargetNullValue
        };

        foreach (var child in source.Bindings)
        {
            if (CloneBindingBase(child) is { } clonedChild)
            {
                clone.Bindings.Add(clonedChild);
            }
        }

        return clone;
    }
}
