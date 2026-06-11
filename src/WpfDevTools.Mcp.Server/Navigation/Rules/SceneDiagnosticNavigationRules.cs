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

    private static IReadOnlyList<ToolNextStep> BuildFormSummary(ToolNavigationContext context)
    {
        var steps = new List<ToolNextStep>();

        if (TryGetRepresentativeBlocker(context.Payload, "commands", out var commandElementId, out var commandReason)
            || TryGetRepresentativeBlocker(context.Payload, "inputs", out commandElementId, out commandReason))
        {
            steps.AddRange(BuildInteractionSteps(commandElementId, commandReason));
        }

        return steps;
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

    private static ToolNextStep CreateAction(
        string tool,
        int priority,
        string reason,
        params (string name, object? value)[] parameters) =>
        new(tool, NavigationParamBuilders.Create(parameters), reason, ToolNextStepKind.Action, priority);

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
