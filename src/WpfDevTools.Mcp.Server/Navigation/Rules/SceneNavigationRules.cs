using System.Text.Json;
using WpfDevTools.Mcp.Server.Navigation.ContextRefs;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation.Rules;

internal static class SceneNavigationRules
{
    public static void Register(ToolNavigationRegistry registry)
    {
        registry.Register("get_ui_summary", BuildUiSummary);
        registry.Register("get_element_snapshot", BuildElementSnapshot);
        registry.Register("capture_state_snapshot", BuildCaptureStateSnapshot);
        registry.Register("get_state_diff", BuildStateDiff);
    }

    private static ToolNavigationEnvelope BuildUiSummary(ToolNavigationContext context)
    {
        if (!TryGetArray(context.Payload, "nodes", out var nodes))
        {
            return ToolNavigationEnvelope.Empty;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            if (!TryGetString(node, "elementId", out var elementId))
            {
                continue;
            }

            if (HasAnnotation(node, "disabled"))
            {
                return ToolNavigationEnvelope.FromRecommended(
                    [
                        CreateDiagnostic(
                        "get_interaction_readiness",
                        1,
                        "Inspect why the disabled UI control is not ready for interaction.",
                        context,
                        ("elementId", elementId))
                    ]);
            }

            if (HasAnnotation(node, "visibility:") || HasAnnotation(node, "transparent"))
            {
                var rootCause = HasAnnotation(node, "transparent")
                    ? "transparent"
                    : "hidden";
                return ToolNavigationEnvelope.FromRecommended(
                    [
                        CreateDiagnostic(
                        "diagnose_visibility",
                        1,
                        "Inspect why the element is hidden or transparent in the scene summary.",
                        context,
                        ("elementId", elementId))
                    ],
                    contextRefs: [VisibilityIssueContextRefBuilder.Create(elementId, rootCause)]);
            }
        }

        if (TryGetString(context.Payload, "summaryText", out var summaryText)
            && summaryText.Contains("binding", StringComparison.OrdinalIgnoreCase))
        {
            return ToolNavigationEnvelope.FromRecommended(
                [
                    CreateDiagnostic(
                    "get_binding_errors",
                    2,
                    "Inspect binding diagnostics hinted by the scene summary.",
                    context)
                ]);
        }

        return ToolNavigationEnvelope.Empty;
    }

    private static IReadOnlyList<ToolNextStep> BuildElementSnapshot(ToolNavigationContext context)
    {
        if (!TryResolveElementId(context, out var elementId))
        {
            return Array.Empty<ToolNextStep>();
        }

        var steps = new List<ToolNextStep>();
        if (TryGetArray(context.Payload, "bindings", out var bindings))
        {
            foreach (var binding in bindings.EnumerateArray())
            {
                if (!TryGetString(binding, "status", out var status)
                    || !string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase)
                    || !TryGetString(binding, "propertyName", out var propertyName))
                {
                    continue;
                }

                steps.Add(CreateDiagnostic(
                    "get_binding_value_chain",
                    1,
                    "Trace the failing binding surfaced by the element snapshot.",
                    context,
                    ("elementId", elementId),
                    ("propertyName", propertyName)));
                break;
            }
        }

        if (HasNonEmptyArray(context.Payload, "validationErrors"))
        {
            steps.Add(CreateDiagnostic(
                "get_validation_errors",
                2,
                "Inspect validation details for the snapped element.",
                context,
                ("elementId", elementId)));
        }

        return steps;
    }

    private static ToolNavigationEnvelope BuildCaptureStateSnapshot(ToolNavigationContext context)
    {
        if (!TryGetString(context.Payload, "snapshotId", out var snapshotId))
        {
            return ToolNavigationEnvelope.Empty;
        }

        return ToolNavigationEnvelope.FromRecommended(
            [
                ConditionalNavigationRules.CreateActiveSnapshotStep(
                "get_state_diff",
                BuildScopedParams(context, ("snapshotId", snapshotId)),
                "Compare the snapshot against current runtime state after mutations.",
                ToolNextStepKind.Verification,
                1,
                "Returns semantic changes since the captured snapshot.",
                "restore_state_snapshot"),
            ],
            alternatives:
            [
                CreateAction(
                "restore_state_snapshot",
                2,
                "Restore the captured snapshot when you need to roll back changes.",
                context,
                ("snapshotId", snapshotId))
            ],
            contextRefs:
            [
                MutationSessionContextRefBuilder.Create(snapshotId, "capture_state_snapshot")
            ]);
    }

    private static ToolNavigationEnvelope BuildStateDiff(ToolNavigationContext context)
    {
        if (!TryGetString(context.Payload, "snapshotId", out var snapshotId))
        {
            return ToolNavigationEnvelope.Empty;
        }

        if (HasMeaningfulChanges(context.Payload))
        {
            return ToolNavigationEnvelope.FromRecommended(
                [
                    CreateAction(
                    "restore_state_snapshot",
                    1,
                    "Restore the snapshot to roll back the detected runtime changes.",
                    context,
                    ("snapshotId", snapshotId))
                ],
                contextRefs:
                [
                    MutationSessionContextRefBuilder.Create(snapshotId, "get_state_diff")
                ]);
        }

        return ToolNavigationEnvelope.FromRecommended(
            [
                CreateDiagnostic(
                "get_ui_summary",
                1,
                "Refresh the scene summary when no tracked changes were detected.",
                context)
            ],
            contextRefs:
            [
                MutationSessionContextRefBuilder.Create(snapshotId, "get_state_diff")
            ]);
    }

    private static bool HasMeaningfulChanges(JsonElement payload) =>
        HasNonEmptyArray(payload, "propertyChanges")
        || HasNonEmptyArray(payload, "viewModelChanges")
        || HasNonEmptyArray(payload, "newBindingErrors")
        || HasNonEmptyArray(payload, "resolvedBindingErrors")
        || HasNonEmptyArray(payload, "validationChanges")
        || (payload.TryGetProperty("focusChange", out var focusChange)
            && focusChange.ValueKind == JsonValueKind.Object
            && focusChange.TryGetProperty("changed", out var changed)
            && changed.ValueKind == JsonValueKind.True);

    private static ToolNextStep CreateDiagnostic(
        string tool,
        int priority,
        string reason,
        ToolNavigationContext context,
        params (string name, object? value)[] extraParameters) =>
        new(
            tool,
            BuildScopedParams(context, extraParameters),
            reason,
            ToolNextStepKind.Diagnostic,
            priority);

    private static ToolNextStep CreateAction(
        string tool,
        int priority,
        string reason,
        ToolNavigationContext context,
        params (string name, object? value)[] extraParameters) =>
        new(
            tool,
            BuildScopedParams(context, extraParameters),
            reason,
            ToolNextStepKind.Action,
            priority);

    private static JsonElement BuildScopedParams(ToolNavigationContext context, params (string name, object? value)[] extraParameters)
    {
        var parameters = new List<(string name, object? value)>
        {
            ("processId", TryGetInt(context.Arguments, "processId"))
        };
        parameters.AddRange(extraParameters);
        return NavigationParamBuilders.Create(parameters.ToArray());
    }

    private static bool HasAnnotation(JsonElement node, string needle)
    {
        if (!TryGetArray(node, "annotations", out var annotations))
        {
            return false;
        }

        return annotations.EnumerateArray().Any(annotation =>
            annotation.ValueKind == JsonValueKind.String
            && annotation.GetString()!.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveElementId(ToolNavigationContext context, out string elementId)
    {
        if (TryGetString(context.Payload, "elementId", out elementId))
        {
            return true;
        }

        if (TryGetString(context.Arguments, "elementId", out elementId))
        {
            return true;
        }

        elementId = string.Empty;
        return false;
    }

    private static bool HasNonEmptyArray(JsonElement element, string propertyName) =>
        TryGetArray(element, propertyName, out var property) && property.GetArrayLength() > 0;

    private static bool TryGetArray(JsonElement? element, string propertyName, out JsonElement property)
    {
        if (element is { } candidate
            && candidate.ValueKind == JsonValueKind.Object
            && candidate.TryGetProperty(propertyName, out property)
            && property.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        property = default;
        return false;
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
