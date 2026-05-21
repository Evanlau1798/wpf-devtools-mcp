using System.Text.Json;
using WpfDevTools.Mcp.Server.Navigation.ContextRefs;
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
        registry.Register("batch_mutate", BuildBatchMutate);
        RegisterSnapshotAwareMutation(registry, "clear_dp_value");
        RegisterSnapshotAwareMutation(registry, "wait_for_dp_change_after_mutation");
        RegisterSnapshotAwareMutation(registry, "force_binding_update");
        RegisterSnapshotAwareMutation(registry, "focus_element");
        RegisterSnapshotAwareMutation(registry, "drag_and_drop");
        RegisterSnapshotAwareMutation(registry, "scroll_to_element");
        RegisterSnapshotAwareMutation(registry, "simulate_keyboard");
        RegisterSnapshotAwareMutation(registry, "override_style_setter");
        RegisterSnapshotAwareMutation(registry, "invalidate_layout");
    }

    private static void RegisterSnapshotAwareMutation(ToolNavigationRegistry registry, string toolName) =>
        registry.Register(toolName, context => BuildSnapshotAwareUiVerification(
            context,
            toolName,
            $"Inspect the updated UI state after {toolName}.",
            $"Returns semantic runtime changes caused by {toolName}."));

    private static ToolNavigationEnvelope BuildClickElement(ToolNavigationContext context) =>
        BuildSnapshotAwareUiVerification(
            context,
            "click_element",
            "Inspect the updated UI state after the click.",
            "Returns semantic runtime changes caused by the click.");

    private static ToolNavigationEnvelope BuildExecuteCommand(ToolNavigationContext context) =>
        BuildSnapshotAwareUiVerification(
            context,
            "execute_command",
            "Inspect the updated UI state after command execution.",
            "Returns semantic runtime changes caused by the command execution.");

    private static ToolNavigationEnvelope BuildFireRoutedEvent(ToolNavigationContext context)
    {
        if (IsUnsuccessfulOrUnknownMutation(context.Payload))
        {
            return ToolNavigationEnvelope.Empty;
        }

        var recommended = new List<ToolNextStep>();
        ToolNavigationReference? mutationContext = null;

        if (TryGetActiveTrace(context.SessionState, out var activeTrace))
        {
            recommended.Add(ConditionalNavigationRules.CreateActiveTraceStep(
                "drain_events",
                NavigationParamBuilders.Create(
                    ("processId", TryGetInt(context.Arguments, "processId")),
                    ("elementId", activeTrace?.ElementId ?? TryGetOptionalString(context.Arguments, "elementId")),
                    ("eventTypes", new[] { "RoutedEvent" })),
                "A routed-event trace is already active; drain the buffered routed-event records now.",
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
            recommended.Add(snapshotStep);
            mutationContext = CreateMutationSessionContext(context, "fire_routed_event");
        }

        if (recommended.Count == 0)
        {
            return BuildUiSummaryVerification(context, "Inspect the updated UI state after the routed event fired.");
        }

        return ToolNavigationEnvelope.FromRecommended(
            recommended,
            contextRefs: mutationContext is null ? [] : [mutationContext]);
    }

    private static ToolNavigationEnvelope BuildModifyViewModel(ToolNavigationContext context)
    {
        if (IsUnsuccessfulOrUnknownMutation(context.Payload))
        {
            return ToolNavigationEnvelope.Empty;
        }

        if (TryBuildStateDiffStep(
                context,
                1,
                "A snapshot is active; compare it against the current runtime state after the ViewModel mutation.",
                "Returns semantic runtime changes caused by the ViewModel mutation.",
                out var snapshotStep))
        {
            return ToolNavigationEnvelope.FromRecommended(
                [snapshotStep],
                contextRefs: [CreateMutationSessionContext(context, "modify_viewmodel")!]);
        }

        var parameters = new List<(string name, object? value)>
        {
            ("processId", TryGetInt(context.Arguments, "processId"))
        };

        if (TryGetString(context.Arguments, "elementId", out var elementId))
        {
            parameters.Add(("elementId", elementId));
        }

        return ToolNavigationEnvelope.FromRecommended(
            [
                new ToolNextStep(
                "get_bindings",
                NavigationParamBuilders.Create(parameters.ToArray()),
                "Inspect the binding state after the ViewModel mutation.",
                ToolNextStepKind.Diagnostic,
                1)
            ]);
    }

    private static ToolNavigationEnvelope BuildSetDpValue(ToolNavigationContext context)
    {
        if (IsUnsuccessfulOrUnknownMutation(context.Payload))
        {
            return ToolNavigationEnvelope.Empty;
        }

        if (!TryGetString(context.Arguments, "propertyName", out var propertyName))
        {
            return ToolNavigationEnvelope.Empty;
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

        return ToolNavigationEnvelope.FromRecommended(
            steps,
            contextRefs: context.SessionState?.ActiveSnapshotId is null
                ? []
                : [CreateMutationSessionContext(context, "set_dp_value")!]);
    }

    private static ToolNavigationEnvelope BuildBatchMutate(ToolNavigationContext context)
    {
        if (TryGetBool(context.Payload, "success", out var success) && !success)
        {
            return ToolNavigationEnvelope.Empty;
        }

        if (!TryGetString(context.Payload, "snapshotId", out var snapshotId))
        {
            return BuildUiSummaryVerification(context, "Inspect the updated UI state after the batch mutations.");
        }

        var recommended = new List<ToolNextStep>
        {
            new(
                "restore_state_snapshot",
                NavigationParamBuilders.Create(
                    ("processId", TryGetInt(context.Arguments, "processId")),
                    ("snapshotId", snapshotId)),
                "Restore the captured snapshot when the batch mutations should be rolled back.",
                ToolNextStepKind.Action,
                1),
            new(
                "get_ui_summary",
                NavigationParamBuilders.Create(
                    ("processId", TryGetInt(context.Arguments, "processId")),
                    ("elementId", TryGetOptionalString(context.Arguments, "elementId"))),
                "Inspect the updated UI state after the batch mutations.",
                ToolNextStepKind.Verification,
                2)
        };

        if (!context.Payload.TryGetProperty("stateDiff", out _))
        {
            recommended.Add(ConditionalNavigationRules.CreateActiveSnapshotStep(
                "get_state_diff",
                NavigationParamBuilders.Create(
                    ("processId", TryGetInt(context.Arguments, "processId")),
                    ("snapshotId", snapshotId)),
                "Compare the batch snapshot against current runtime state after the mutations.",
                ToolNextStepKind.Verification,
                3,
                "Returns semantic runtime changes caused by the batch mutations.",
                "restore_state_snapshot"));
        }

        return ToolNavigationEnvelope.FromRecommended(
            recommended,
            contextRefs: [MutationSessionContextRefBuilder.Create(snapshotId, "batch_mutate")]);
    }

    private static ToolNavigationEnvelope BuildUiSummaryVerification(ToolNavigationContext context, string reason) =>
        ToolNavigationEnvelope.FromRecommended(
            [
                new ToolNextStep(
                "get_ui_summary",
                NavigationParamBuilders.Create(
                    ("processId", TryGetInt(context.Arguments, "processId")),
                    ("elementId", TryGetOptionalString(context.Arguments, "elementId"))),
                reason,
                ToolNextStepKind.Verification,
                1)
            ]);

    private static ToolNavigationEnvelope BuildSnapshotAwareUiVerification(
        ToolNavigationContext context,
        string sourceTool,
        string fallbackReason,
        string expectedOutcome)
    {
        if (IsUnsuccessfulOrUnknownMutation(context.Payload))
        {
            return ToolNavigationEnvelope.Empty;
        }

        if (TryBuildStateDiffStep(
                context,
                1,
                "A snapshot is active; compare it against the current runtime state after the last action.",
                expectedOutcome,
                out var snapshotStep))
        {
            return ToolNavigationEnvelope.FromRecommended(
                [snapshotStep],
                contextRefs: [CreateMutationSessionContext(context, sourceTool)!]);
        }

        return BuildUiSummaryVerification(context, fallbackReason);
    }

    private static bool IsUnsuccessfulOrUnknownMutation(JsonElement? payload) =>
        (TryGetBool(payload, "success", out var success) && !success)
        || (TryGetBool(payload, "requiresReconnect", out var requiresReconnect) && requiresReconnect)
        || (TryGetBool(payload, "stateAfterTimeoutUnknown", out var stateAfterTimeoutUnknown) && stateAfterTimeoutUnknown);

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

    private static ToolNavigationReference? CreateMutationSessionContext(ToolNavigationContext context, string sourceTool) =>
        string.IsNullOrWhiteSpace(context.SessionState?.ActiveSnapshotId)
            ? null
            : MutationSessionContextRefBuilder.Create(context.SessionState!.ActiveSnapshotId!, sourceTool);

    private static bool TryGetActiveTrace(NavigationSessionState? sessionState, out ActiveTraceNavigationState? traceState)
    {
        traceState = sessionState?.ActiveTrace;
        if (traceState is null
            || traceState.HasExpired(DateTimeOffset.UtcNow)
            || traceState.FollowUpExpiresAtUtc.HasValue)
        {
            traceState = null;
            return false;
        }

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

    private static bool TryGetBool(JsonElement? element, string propertyName, out bool value)
    {
        if (element is { } candidate
            && candidate.ValueKind == JsonValueKind.Object
            && candidate.TryGetProperty(propertyName, out var property)
            && (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False))
        {
            value = property.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }
}
