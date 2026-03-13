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
        BuildSnapshotAwareUiVerification(
            context,
            "Inspect the updated UI state after the click.",
            "Returns semantic runtime changes caused by the click.");

    private static IReadOnlyList<ToolNextStep> BuildExecuteCommand(ToolNavigationContext context) =>
        BuildSnapshotAwareUiVerification(
            context,
            "Inspect the updated UI state after command execution.",
            "Returns semantic runtime changes caused by the command execution.");

    private static IReadOnlyList<ToolNextStep> BuildFireRoutedEvent(ToolNavigationContext context)
    {
        var steps = new List<ToolNextStep>();

        if (context.SessionState?.ActiveTrace is not null)
        {
            steps.Add(ConditionalNavigationRules.CreateActiveTraceStep(
                "trace_routed_events",
                NavigationParamBuilders.Create(
                    ("processId", TryGetInt(context.Arguments, "processId")),
                    ("mode", "get")),
                "A routed-event trace is already active; retrieve the buffered trace now.",
                ToolNextStepKind.Verification,
                1,
                "Returns buffered routed-event records collected after the mutation."));
        }

        if (TryBuildStateDiffStep(
                context,
                2,
                "A snapshot is active; compare it against the current runtime state after the routed event.",
                "Returns semantic runtime changes caused by the routed event.",
                out var snapshotStep))
        {
            steps.Add(snapshotStep);
        }

        if (steps.Count == 0)
        {
            steps.AddRange(BuildUiSummaryVerification(context, "Inspect the updated UI state after the routed event fired."));
        }

        return steps;
    }

    private static IReadOnlyList<ToolNextStep> BuildModifyViewModel(ToolNavigationContext context)
    {
        if (TryBuildStateDiffStep(
                context,
                1,
                "A snapshot is active; compare it against the current runtime state after the ViewModel mutation.",
                "Returns semantic runtime changes caused by the ViewModel mutation.",
                out var snapshotStep))
        {
            return [snapshotStep];
        }

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

        var steps = new List<ToolNextStep>
        {
            new ToolNextStep(
                "get_dp_value_source",
                NavigationParamBuilders.Create(
                    ("processId", TryGetInt(context.Arguments, "processId")),
                    ("elementId", TryGetOptionalString(context.Arguments, "elementId")),
                    ("propertyName", propertyName)),
                "Verify the dependency property value source after the mutation.",
                ToolNextStepKind.Verification,
                1)
        };

        if (TryBuildStateDiffStep(
                context,
                2,
                "A snapshot is active; compare it against the current runtime state after the dependency property mutation.",
                "Returns semantic runtime changes caused by the dependency property mutation.",
                out var snapshotStep))
        {
            steps.Add(snapshotStep);
        }

        return steps;
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

    private static IReadOnlyList<ToolNextStep> BuildSnapshotAwareUiVerification(
        ToolNavigationContext context,
        string fallbackReason,
        string expectedOutcome)
    {
        if (TryBuildStateDiffStep(
                context,
                1,
                "A snapshot is active; compare it against the current runtime state after the last action.",
                expectedOutcome,
                out var snapshotStep))
        {
            return [snapshotStep];
        }

        return BuildUiSummaryVerification(context, fallbackReason);
    }

    private static bool TryBuildStateDiffStep(
        ToolNavigationContext context,
        int priority,
        string reason,
        string expectedOutcome,
        out ToolNextStep step)
    {
        if (string.IsNullOrWhiteSpace(context.SessionState?.ActiveSnapshotId))
        {
            step = null!;
            return false;
        }

        step = ConditionalNavigationRules.CreateActiveSnapshotStep(
            "get_state_diff",
            NavigationParamBuilders.Create(
                ("processId", TryGetInt(context.Arguments, "processId")),
                ("snapshotId", context.SessionState!.ActiveSnapshotId)),
            reason,
            ToolNextStepKind.Verification,
            priority,
            expectedOutcome,
            "restore_state_snapshot");
        return true;
    }

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
