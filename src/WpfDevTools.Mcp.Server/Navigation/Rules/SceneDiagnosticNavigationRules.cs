using System.Text.Json;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation.Rules;

internal static class SceneDiagnosticNavigationRules
{
    public static void Register(ToolNavigationRegistry registry)
    {
        registry.Register("diagnose_visibility", BuildDiagnoseVisibility);
        registry.Register("get_interaction_readiness", BuildInteractionReadiness);
        registry.Register("get_form_summary", BuildFormSummary);
    }

    private static IReadOnlyList<ToolNextStep> BuildDiagnoseVisibility(ToolNavigationContext context)
    {
        if (!TryResolveElementId(context, out var elementId) || !TryGetString(context.Payload, "rootCause", out var rootCause))
        {
            return Array.Empty<ToolNextStep>();
        }

        if (rootCause.Contains("ancestor", StringComparison.OrdinalIgnoreCase)
            && rootCause.Contains("visibility", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                CreateDiagnostic(
                    "get_dp_value_source",
                    1,
                    "Inspect visibility precedence before mutating an ancestor-owned visibility issue.",
                    ("elementId", elementId),
                    ("propertyName", "Visibility"))
            ];
        }

        if (ContainsAny(rootCause, "visibility=hidden", "visibility=collapsed"))
        {
            return
            [
                CreateAction(
                    "set_dp_value",
                    1,
                    "Restore the element visibility to Visible.",
                    ("elementId", elementId),
                    ("propertyName", "Visibility"),
                    ("value", "Visible"))
            ];
        }

        if (ContainsAny(rootCause, "opacity=0", "opacity 0"))
        {
            return
            [
                CreateAction(
                    "set_dp_value",
                    1,
                    "Restore opacity so the element becomes visible again.",
                    ("elementId", elementId),
                    ("propertyName", "Opacity"),
                    ("value", 1))
            ];
        }

        return Array.Empty<ToolNextStep>();
    }

    private static IReadOnlyList<ToolNextStep> BuildInteractionReadiness(ToolNavigationContext context)
    {
        if (!TryResolveElementId(context, out var elementId) || !TryGetArray(context.Payload, "blockers", out var blockers))
        {
            return Array.Empty<ToolNextStep>();
        }

        var steps = new List<ToolNextStep>();
        foreach (var blocker in blockers.EnumerateArray())
        {
            var reason = ExtractReason(blocker);
            if (string.IsNullOrWhiteSpace(reason))
            {
                continue;
            }

            if (string.Equals(reason, "ElementDisabled", StringComparison.Ordinal))
            {
                AddUnique(steps, CreateDiagnostic(
                    "get_dp_value_source",
                    1,
                    "Inspect why the element is disabled.",
                    ("elementId", elementId),
                    ("propertyName", "IsEnabled")));
            }

            if (string.Equals(reason, "CommandCannotExecute", StringComparison.Ordinal))
            {
                AddUnique(steps, CreateDiagnostic(
                    "get_commands",
                    2,
                    "Inspect ICommand.CanExecute for the blocked element.",
                    ("elementId", elementId)));
            }

            if (string.Equals(reason, "ElementInInactiveTab", StringComparison.Ordinal)
                && TryGetNestedString(context.Payload, "activationTarget", "tabItemElementId", out var tabItemElementId))
            {
                AddUnique(steps, CreateAction(
                    "click_element",
                    1,
                    "Activate the containing TabItem before retrying interaction on this element.",
                    ("elementId", tabItemElementId)));
            }

            if (reason.Contains("Visibility", StringComparison.OrdinalIgnoreCase))
            {
                AddUnique(steps, CreateDiagnostic(
                    "diagnose_visibility",
                    3,
                    "Inspect why the element is not visible enough for interaction.",
                    ("elementId", elementId)));
            }
        }

        return steps;
    }

    private static ToolNavigationEnvelope BuildFormSummary(ToolNavigationContext context)
    {
        var steps = new List<ToolNextStep>();

        if (TryGetRepresentativeBlocker(context.Payload, "commands", out var commandElementId, out var commandReason)
            || TryGetRepresentativeBlocker(context.Payload, "inputs", out commandElementId, out commandReason))
        {
            steps.AddRange(BuildInteractionSteps(commandElementId, commandReason));
        }

        if (steps.Count > 0)
        {
            return ToolNavigationEnvelope.FromRecommended(steps);
        }

        if (IsTruncated(context.Payload)
            && TryGetString(context.Payload, "formScope", out var formScope))
        {
            return ToolNavigationEnvelope.FromRecommended(
                [
                    CreateDiagnostic(
                        context,
                        "get_namescope",
                        1,
                        "The form summary was truncated; inspect the scoped namescope before treating the form summary as a complete inventory.",
                        ("elementId", formScope))
                ],
                alternatives:
                [
                    CreateDiagnostic(
                        context,
                        "get_ui_summary",
                        2,
                        "Use a scoped scene summary to re-orient within the truncated form subtree.",
                        ("elementId", formScope),
                        ("depthMode", "semantic"),
                        ("summaryOnly", true)),
                    CreateDiagnostic(
                        context,
                        "get_visual_tree",
                        3,
                        "Inspect a capped visual subtree after narrowing from the truncated form summary.",
                        ("elementId", formScope),
                        ("depth", 4),
                        ("compact", true),
                        ("maxNodes", 220))
                ],
                prefetchTools: ["find_elements"]);
        }

        return ToolNavigationEnvelope.Empty;
    }

    private static IEnumerable<ToolNextStep> BuildInteractionSteps(string elementId, string reason)
    {
        if (string.Equals(reason, "ElementDisabled", StringComparison.Ordinal))
        {
            yield return CreateDiagnostic(
                "get_dp_value_source",
                1,
                "Inspect why the representative form control is disabled.",
                ("elementId", elementId),
                ("propertyName", "IsEnabled"));
            yield break;
        }

        if (string.Equals(reason, "CommandCannotExecute", StringComparison.Ordinal))
        {
            yield return CreateDiagnostic(
                "get_commands",
                1,
                "Inspect ICommand.CanExecute for the representative form action.",
                ("elementId", elementId));
            yield break;
        }

        if (reason.Contains("Visibility", StringComparison.OrdinalIgnoreCase))
        {
            yield return CreateDiagnostic(
                "diagnose_visibility",
                1,
                "Inspect why the representative form control is hidden or clipped.",
                ("elementId", elementId));
        }
    }

    private static bool TryGetRepresentativeBlocker(
        JsonElement payload,
        string propertyName,
        out string elementId,
        out string reason)
    {
        if (!TryGetArray(payload, propertyName, out var nodes))
        {
            elementId = string.Empty;
            reason = string.Empty;
            return false;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            if (!TryGetString(node, "elementId", out elementId) || !TryGetBlockerReason(node, out reason))
            {
                continue;
            }

            return true;
        }

        elementId = string.Empty;
        reason = string.Empty;
        return false;
    }

    private static bool TryGetBlockerReason(JsonElement node, out string reason)
    {
        if (!TryGetArray(node, "blockers", out var blockers))
        {
            reason = string.Empty;
            return false;
        }

        foreach (var blocker in blockers.EnumerateArray())
        {
            reason = ExtractReason(blocker);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    private static string ExtractReason(JsonElement blocker)
    {
        if (blocker.ValueKind == JsonValueKind.String)
        {
            return blocker.GetString() ?? string.Empty;
        }

        return TryGetString(blocker, "reason", out var reason) ? reason : string.Empty;
    }

    private static ToolNextStep CreateDiagnostic(
        string tool,
        int priority,
        string reason,
        params (string name, object? value)[] parameters) =>
        new(tool, NavigationParamBuilders.Create(parameters), reason, ToolNextStepKind.Diagnostic, priority);

    private static ToolNextStep CreateDiagnostic(
        ToolNavigationContext context,
        string tool,
        int priority,
        string reason,
        params (string name, object? value)[] parameters) =>
        new(tool, BuildScopedParams(context, parameters), reason, ToolNextStepKind.Diagnostic, priority);

    private static ToolNextStep CreateAction(
        string tool,
        int priority,
        string reason,
        params (string name, object? value)[] parameters) =>
        new(tool, NavigationParamBuilders.Create(parameters), reason, ToolNextStepKind.Action, priority);

    private static JsonElement BuildScopedParams(
        ToolNavigationContext context,
        params (string name, object? value)[] extraParameters)
    {
        var parameters = new List<(string name, object? value)>
        {
            ("processId", TryGetInt(context.Arguments, "processId"))
        };
        parameters.AddRange(extraParameters);
        return NavigationParamBuilders.Create(parameters.ToArray());
    }

    private static bool TryResolveElementId(ToolNavigationContext context, out string elementId)
    {
        if (TryGetString(context.Payload, "elementId", out elementId))
        {
            return true;
        }

        if (context.Arguments is { } arguments && TryGetString(arguments, "elementId", out elementId))
        {
            return true;
        }

        elementId = string.Empty;
        return false;
    }

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.TryGetProperty(propertyName, out property) && property.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        property = default;
        return false;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            var candidate = property.GetString();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                value = candidate;
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

    private static bool IsTruncated(JsonElement element) =>
        element.TryGetProperty("truncated", out var property)
        && property.ValueKind == JsonValueKind.True;

    private static bool TryGetNestedString(
        JsonElement element,
        string objectPropertyName,
        string nestedPropertyName,
        out string value)
    {
        if (element.TryGetProperty(objectPropertyName, out var objectProperty)
            && objectProperty.ValueKind == JsonValueKind.Object
            && objectProperty.TryGetProperty(nestedPropertyName, out var nestedProperty)
            && nestedProperty.ValueKind == JsonValueKind.String)
        {
            var candidate = nestedProperty.GetString();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                value = candidate;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static void AddUnique(List<ToolNextStep> steps, ToolNextStep step)
    {
        if (steps.Any(existing => existing.Tool == step.Tool && existing.Params.GetRawText() == step.Params.GetRawText()))
        {
            return;
        }

        steps.Add(step);
    }
}
