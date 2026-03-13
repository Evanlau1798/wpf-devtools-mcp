using System.Text.Json;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation.Rules;

internal static class ActionNavigationRules
{
    public static void Register(ToolNavigationRegistry registry)
    {
        registry.Register("click_element", BuildClickElement);
        registry.Register("execute_command", BuildExecuteCommand);
        registry.Register("modify_viewmodel", BuildModifyViewModel);
        registry.Register("set_dp_value", BuildSetDpValue);
        registry.Register("fire_routed_event", BuildFireRoutedEvent);
    }

    private static IReadOnlyList<ToolNextStep> BuildClickElement(ToolNavigationContext context) =>
        BuildUiSummaryVerification(context, "Inspect the updated UI state after the click.");

    private static IReadOnlyList<ToolNextStep> BuildExecuteCommand(ToolNavigationContext context) =>
        BuildUiSummaryVerification(context, "Inspect the updated UI state after command execution.");

    private static IReadOnlyList<ToolNextStep> BuildFireRoutedEvent(ToolNavigationContext context) =>
        BuildUiSummaryVerification(context, "Inspect the updated UI state after the routed event fired.");

    private static IReadOnlyList<ToolNextStep> BuildModifyViewModel(ToolNavigationContext context)
    {
        var parameters = new List<(string name, object? value)>
        {
            ("processId", TryGetInt(context.Arguments, "processId"))
        };

        if (TryGetString(context.Arguments, "elementId", out var elementId))
        {
            parameters.Add(("elementId", elementId));
        }

        return
        [
            new ToolNextStep(
                "get_bindings",
                NavigationParamBuilders.Create(parameters.ToArray()),
                "Inspect the binding state after the ViewModel mutation.",
                ToolNextStepKind.Diagnostic,
                1)
        ];
    }

    private static IReadOnlyList<ToolNextStep> BuildSetDpValue(ToolNavigationContext context)
    {
        if (!TryGetString(context.Arguments, "propertyName", out var propertyName))
        {
            return Array.Empty<ToolNextStep>();
        }

        return
        [
            new ToolNextStep(
                "get_dp_value_source",
                NavigationParamBuilders.Create(
                    ("processId", TryGetInt(context.Arguments, "processId")),
                    ("elementId", TryGetOptionalString(context.Arguments, "elementId")),
                    ("propertyName", propertyName)),
                "Verify the dependency property value source after the mutation.",
                ToolNextStepKind.Verification,
                1)
        ];
    }

    private static IReadOnlyList<ToolNextStep> BuildUiSummaryVerification(ToolNavigationContext context, string reason) =>
        [
            new ToolNextStep(
                "get_ui_summary",
                NavigationParamBuilders.Create(("processId", TryGetInt(context.Arguments, "processId"))),
                reason,
                ToolNextStepKind.Verification,
                1)
        ];

    private static bool TryGetString(JsonElement? element, string propertyName, out string value)
    {
        if (element is { } candidate
            && candidate.ValueKind == JsonValueKind.Object
            && candidate.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String)
        {
            var text = property.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                value = text;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string? TryGetOptionalString(JsonElement? element, string propertyName) =>
        TryGetString(element, propertyName, out var value) ? value : null;

    private static int? TryGetInt(JsonElement? element, string propertyName)
    {
        if (element is { } candidate
            && candidate.ValueKind == JsonValueKind.Object
            && candidate.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var value))
        {
            return value;
        }

        return null;
    }
}
