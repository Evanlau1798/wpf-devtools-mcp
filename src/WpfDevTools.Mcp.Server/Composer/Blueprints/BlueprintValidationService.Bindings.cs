using System.Text.Json;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal sealed partial class BlueprintValidationService
{
    private static readonly Regex BindingPropertyNamePattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)?$",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ReservedBindingPropertyNames = new(StringComparer.Ordinal)
    {
        "Name",
        "x:Name",
        "AutomationProperties.AutomationId"
    };

    private static void ValidateBindings(
        UiBlueprintNode node,
        string path,
        List<BlueprintValidationIssue> errors)
    {
        foreach (var (propertyName, value) in node.Bindings)
        {
            var bindingPath = $"{path}.bindings.{propertyName}";
            if (!BindingPropertyNamePattern.IsMatch(propertyName)
                || ReservedBindingPropertyNames.Contains(propertyName))
            {
                errors.Add(Issue(
                    bindingPath,
                    "InvalidBindingPropertyName",
                    $"Binding property name '{propertyName}' is not a safe XAML property name.",
                    "Use an ordinary dependency property name such as Text, Command, ItemsSource, or SelectedItem."));
                continue;
            }

            if (value.ValueKind != JsonValueKind.String
                || !ViewModelBindingRequirementBuilder.TryNormalizeBindingPath(
                    value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty,
                    out _))
            {
                errors.Add(Issue(
                    bindingPath,
                    "BindingExpressionInvalid",
                    $"Binding '{propertyName}' must be a WPF Binding markup expression.",
                    $"Set bindings.{propertyName} to a value such as '{{Binding PropertyName}}'."));
            }
        }
    }
}
