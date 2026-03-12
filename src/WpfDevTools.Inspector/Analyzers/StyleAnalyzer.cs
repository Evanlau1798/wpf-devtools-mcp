using System.Windows;
using System.Windows.Controls;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Inspector.Analyzers;

/// <summary>
/// Analyzes WPF styles, triggers, and templates
/// </summary>
public sealed class StyleAnalyzer : DispatcherAnalyzerBase
{
    private readonly ElementFinder _elementFinder;

    /// <summary>
    /// Create a new StyleAnalyzer instance
    /// </summary>
    /// <param name="elementFinder">Element finder for locating WPF elements</param>
    public StyleAnalyzer(ElementFinder elementFinder)
    {
        _elementFinder = elementFinder;
    }

    /// <summary>
    /// Get applied styles for an element
    /// </summary>
    public object GetAppliedStyles(string? elementId, bool compact = false)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not FrameworkElement fe)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a FrameworkElement",
                    "Choose a FrameworkElement target from get_visual_tree or find_elements before inspecting styles.");
            }

            var styles = new List<object>();

            // Get explicit style
            if (fe.Style != null)
            {
                styles.Add(compact
                    ? new
                    {
                        styleType = "Explicit",
                        type = "Explicit",
                        targetType = fe.Style.TargetType?.Name,
                        setterCount = fe.Style.Setters.Count,
                        triggerCount = fe.Style.Triggers.Count,
                        hasBasedOn = fe.Style.BasedOn != null
                    }
                    : new
                    {
                        styleType = "Explicit",
                        type = "Explicit",
                        targetType = fe.Style.TargetType?.Name,
                        setters = fe.Style.Setters
                            .OfType<Setter>()
                            .Select(setter => new
                            {
                                property = setter.Property?.Name,
                                value = setter.Value?.ToString()
                            })
                            .ToArray(),
                        setterCount = fe.Style.Setters.Count,
                        triggerCount = fe.Style.Triggers.Count,
                        hasBasedOn = fe.Style.BasedOn != null
                    });
            }

            var localResourceReferences = GetLocalResourceReferences(fe);

            return new
            {
                success = true,
                hasStyle = fe.Style != null,
                styles,
                count = styles.Count,
                styleCount = styles.Count,
                localResourceReferenceCount = localResourceReferences.Count,
                localResourceReferences,
                notes = fe.Style == null && localResourceReferences.Count > 0
                    ? "Element appearance is driven by local resource references rather than an applied Style."
                    : null
            };
        });
    }

    private static List<object> GetLocalResourceReferences(FrameworkElement element)
    {
        var references = new List<object>();
        var enumerator = element.GetLocalValueEnumerator();

        while (enumerator.MoveNext())
        {
            var entry = enumerator.Current;
            var valueTypeName = entry.Value?.GetType().Name;
            if (valueTypeName == null ||
                valueTypeName.IndexOf("ResourceReferenceExpression", StringComparison.Ordinal) < 0)
            {
                continue;
            }

            references.Add(new
            {
                property = entry.Property?.Name,
                expressionType = valueTypeName,
                valueSource = "LocalValue"
            });
        }

        return references;
    }

    /// <summary>
    /// Get triggers for an element
    /// </summary>
    public object GetTriggers(string? elementId)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not FrameworkElement fe)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a FrameworkElement",
                    "Choose a FrameworkElement target before inspecting triggers.");
            }

            var triggers = new List<object>();

            // Get triggers from style
            if (fe.Style != null)
            {
                foreach (var trigger in fe.Style.Triggers)
                {
                    triggers.Add(CreateTriggerInfo(trigger, "Style"));
                }
            }

            if (fe is Control control && control.Template != null)
            {
                foreach (var trigger in control.Template.Triggers)
                {
                    triggers.Add(CreateTriggerInfo(trigger, "ControlTemplate"));
                }
            }

            return new
            {
                success = true,
                triggers,
                count = triggers.Count,
                triggerCount = triggers.Count
            };
        });
    }

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
                    new
                    {
                        property = dataTrigger.Binding?.ToString(),
                        value = dataTrigger.Value?.ToString()
                    }
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
                    .Select(condition => new
                    {
                        property = condition.Binding?.ToString(),
                        value = condition.Value?.ToString()
                    })
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

    /// <summary>
    /// Get template tree for a control
    /// </summary>
    public object GetTemplateTree(string? elementId)
    {
        return InvokeOnUIThread<object>(() =>
        {
            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not Control control)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a Control",
                    "Choose a Control element before calling get_template_tree.");
            }

            if (control.Template == null)
            {
                return new { success = true, message = "Element has no template", hasTemplate = false };
            }

            try
            {
                var templateRoot = control.Template.LoadContent();

                return new
                {
                    success = true,
                    hasTemplate = true,
                    templateType = control.Template.GetType().Name,
                    rootType = templateRoot?.GetType().Name
                };
            }
            catch (Exception ex)
            {
                return ToolErrorFactory.OperationFailed(
                    "load template",
                    ex,
                    "Ensure the target control template is available and the control is fully initialized before retrying.");
            }
        });
    }

    /// <summary>
    /// Get resource resolution chain for an element
    /// </summary>
    public object GetResourceChain(string? elementId, string resourceKey)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                return ToolErrorFactory.InvalidArgument(
                    "resourceKey is required",
                    "Provide the resource key string you want to resolve.");
            }

            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not FrameworkElement fe)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a FrameworkElement",
                    "Choose a FrameworkElement target before resolving a resource chain.");
            }

            var chain = new List<object>();
            var current = fe;

            // Walk up the tree looking for the resource
            while (current != null)
            {
                if (current.Resources.Contains(resourceKey))
                {
                    var resource = current.Resources[resourceKey];
                    chain.Add(new
                    {
                        level = "Element",
                        elementType = current.GetType().Name,
                        resourceKey,
                        resourceType = resource?.GetType().Name,
                        resourceValue = resource?.ToString()
                    });
                    break;
                }

                current = current.Parent as FrameworkElement;
            }

            // Check Application resources
            if (chain.Count == 0 && Application.Current?.Resources.Contains(resourceKey) == true)
            {
                var resource = Application.Current.Resources[resourceKey];
                chain.Add(new
                {
                    level = "Application",
                    elementType = "Application",
                    resourceKey,
                    resourceType = resource?.GetType().Name,
                    resourceValue = resource?.ToString()
                });
            }

            return new
            {
                success = true,
                resourceKey,
                found = chain.Count > 0,
                chain
            };
        });
    }

    /// <summary>
    /// Override a style setter value at runtime
    /// </summary>
    public object OverrideStyleSetter(string? elementId, string propertyName, object value)
    {
        return InvokeOnUIThread<object>(() =>
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return ToolErrorFactory.InvalidArgument(
                    "propertyName is required",
                    "Provide the DependencyProperty name to override.");
            }

            var element = elementId == null
                ? _elementFinder.GetRootElement()
                : _elementFinder.FindById(elementId);

            if (element == null)
            {
                return ToolErrorFactory.ElementNotFound(elementId);
            }

            if (element is not FrameworkElement fe)
            {
                return ToolErrorFactory.InvalidArgument(
                    "Element is not a FrameworkElement",
                    "Choose a FrameworkElement target before overriding a style setter.");
            }

            try
            {
                // Find the DependencyProperty
                var dp = FindDependencyProperty(fe, propertyName);
                if (dp == null)
                {
                    return ToolErrorFactory.PropertyNotFound(propertyName, fe.GetType().Name);
                }

                // Set local value (overrides style)
                var targetType = dp.PropertyType;
                var oldValue = fe.GetValue(dp);
                var localValueBefore = fe.ReadLocalValue(dp);
                var hadLocalValueBefore = localValueBefore != DependencyProperty.UnsetValue;
                var previousValueSource = DependencyPropertyHelper.GetValueSource(fe, dp);
                var convertedValue = ConvertValue(value, targetType);
                AuditLogger.LogSecurityEvent("StyleOverride", $"Property '{propertyName}' overridden on element '{elementId ?? "root"}'");
                fe.SetValue(dp, convertedValue);
                var newValue = fe.GetValue(dp);

                return new
                {
                    success = true,
                    message = $"Style setter for '{propertyName}' overridden with local value",
                    propertyName,
                    oldValue = FormatResponseValue(oldValue),
                    newValue = FormatResponseValue(newValue),
                    hadLocalValueBefore,
                    previousLocalValue = hadLocalValueBefore ? FormatResponseValue(localValueBefore) : null,
                    previousBaseValueSource = previousValueSource.BaseValueSource.ToString(),
                    valueType = newValue?.GetType().Name
                };
            }
            catch (Exception ex)
            {
                return ToolErrorFactory.OperationFailed(
                    "override setter",
                    ex,
                    "Verify the propertyName is style-backed and the provided value is compatible with the target property type.");
            }
        });
    }

}

