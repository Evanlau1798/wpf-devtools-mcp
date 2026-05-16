using System.Windows;
using System.Windows.Data;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class StyleAnalyzer
{
    private static object CreateTriggerInfo(TriggerBase trigger, string source)
    {
        return trigger switch
        {
            Trigger propertyTrigger => new
            {
                source,
                type = nameof(Trigger),
                triggerType = "Property",
                conditions = new[]
                {
                    new
                    {
                        property = propertyTrigger.Property?.Name,
                        value = propertyTrigger.Value?.ToString()
                    }
                },
                setters = CreateSetterInfos(propertyTrigger.Setters)
            },
            DataTrigger dataTrigger => new
            {
                source,
                type = nameof(DataTrigger),
                triggerType = "Data",
                conditions = new[]
                {
                    CreateBindingConditionInfo(dataTrigger.Binding, dataTrigger.Value)
                },
                setters = CreateSetterInfos(dataTrigger.Setters)
            },
            MultiTrigger multiTrigger => new
            {
                source,
                type = nameof(MultiTrigger),
                triggerType = "MultiTrigger",
                conditions = multiTrigger.Conditions
                    .Cast<Condition>()
                    .Select(condition => new
                    {
                        property = condition.Property?.Name,
                        value = condition.Value?.ToString()
                    })
                    .ToArray(),
                setters = CreateSetterInfos(multiTrigger.Setters)
            },
            MultiDataTrigger multiDataTrigger => new
            {
                source,
                type = nameof(MultiDataTrigger),
                triggerType = "MultiTrigger",
                conditions = multiDataTrigger.Conditions
                    .Cast<Condition>()
                    .Select(condition => CreateBindingConditionInfo(condition.Binding, condition.Value))
                    .ToArray(),
                setters = CreateSetterInfos(multiDataTrigger.Setters)
            },
            EventTrigger eventTrigger => new
            {
                source,
                type = nameof(EventTrigger),
                triggerType = "Event",
                conditions = new[]
                {
                    new
                    {
                        property = eventTrigger.RoutedEvent?.Name,
                        value = "Raised"
                    }
                },
                setters = Array.Empty<object>()
            },
            _ => new
            {
                source,
                type = trigger.GetType().Name,
                triggerType = trigger.GetType().Name,
                conditions = Array.Empty<object>(),
                setters = Array.Empty<object>()
            }
        };
    }

    private static object CreateBindingConditionInfo(BindingBase? bindingBase, object? value)
    {
        if (bindingBase is Binding binding)
        {
            return new
            {
                property = binding.Path?.Path ?? bindingBase.ToString(),
                bindingPath = binding.Path?.Path,
                bindingElementName = string.IsNullOrWhiteSpace(binding.ElementName) ? null : binding.ElementName,
                bindingSourceKind = GetBindingSourceKind(binding),
                value = value?.ToString()
            };
        }

        return new
        {
            property = bindingBase?.ToString(),
            bindingPath = (string?)null,
            bindingElementName = (string?)null,
            bindingSourceKind = bindingBase?.GetType().Name,
            value = value?.ToString()
        };
    }

    private static string GetBindingSourceKind(Binding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.ElementName))
        {
            return "ElementName";
        }

        if (binding.RelativeSource != null)
        {
            return "RelativeSource";
        }

        if (binding.Source != null)
        {
            return "Source";
        }

        if (!string.IsNullOrWhiteSpace(binding.XPath))
        {
            return "XPath";
        }

        return "DataContext";
    }

    private static object[] CreateSetterInfos(SetterBaseCollection setters)
    {
        return setters
            .OfType<Setter>()
            .Select(setter => (object)new
            {
                property = setter.Property?.Name,
                value = setter.Value?.ToString()
            })
            .ToArray();
    }
}
