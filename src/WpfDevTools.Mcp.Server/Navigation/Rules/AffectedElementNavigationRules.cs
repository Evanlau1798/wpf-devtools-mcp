using System.Text.Json;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation.Rules;

internal static class AffectedElementNavigationRules
{
    public static void Register(ToolNavigationRegistry registry)
    {
        registry.Register("get_affected_elements", BuildAffectedElements);
    }

    private static ToolNavigationEnvelope BuildAffectedElements(ToolNavigationContext context)
    {
        if (!RequiresVerification(context.Payload)
            || !HasPositiveCount(context.Payload, "affectedCount"))
        {
            return ToolNavigationEnvelope.Empty;
        }

        var recommended = new List<ToolNextStep>
        {
            CreateDiagnostic(
                "get_bindings",
                BuildVerificationParams(context),
                "Verify the candidate list against the actual runtime binding declarations.",
                1)
        };

        var alternatives = new List<ToolNextStep>();
        if (TryGetFirstAffectedElementId(context.Payload, out var firstElementId))
        {
            alternatives.Add(CreateDiagnostic(
                "get_element_snapshot",
                NavigationParamBuilders.Create(("elementId", firstElementId)),
                "Inspect a candidate element in one aggregated scene snapshot.",
                2));
        }

        return ToolNavigationEnvelope.FromRecommended(recommended, alternatives);
    }

    private static JsonElement BuildVerificationParams(ToolNavigationContext context)
    {
        if (context.Arguments is not { } arguments)
        {
            return NavigationParamBuilders.Create(("recursive", true));
        }

        if (TryGetString(arguments, "elementId", out var elementId))
        {
            var recursive = TryGetBool(arguments, "recursive");
            return recursive == false
                ? NavigationParamBuilders.Create(("elementId", elementId))
                : NavigationParamBuilders.Create(("elementId", elementId), ("recursive", true));
        }

        return NavigationParamBuilders.Create(("recursive", true));
    }

    private static ToolNextStep CreateDiagnostic(
        string tool,
        JsonElement parameters,
        string reason,
        int priority) =>
        new(tool, parameters, reason, ToolNextStepKind.Diagnostic, priority);

    private static bool RequiresVerification(JsonElement payload) =>
        payload.TryGetProperty("requiresVerification", out var property)
        && property.ValueKind == JsonValueKind.True;

    private static bool HasPositiveCount(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.GetInt32() > 0;

    private static bool TryGetFirstAffectedElementId(JsonElement payload, out string elementId)
    {
        if (payload.TryGetProperty("affectedElements", out var affectedElements)
            && affectedElements.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in affectedElements.EnumerateArray())
            {
                if (TryGetString(item, "elementId", out elementId))
                {
                    return true;
                }
            }
        }

        elementId = string.Empty;
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

    private static bool? TryGetBool(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return property.GetBoolean();
        }

        return null;
    }
}
