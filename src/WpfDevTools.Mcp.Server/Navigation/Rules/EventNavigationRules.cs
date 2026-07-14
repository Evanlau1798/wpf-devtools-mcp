using System.Text.Json;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation.Rules;

internal static class EventNavigationRules
{
    public static void Register(ToolNavigationRegistry registry)
    {
        registry.Register("get_event_handlers", BuildGetEventHandlers);
        registry.Register("trace_routed_events", BuildTraceRoutedEvents);
    }

    private static IReadOnlyList<ToolNextStep> BuildTraceRoutedEvents(ToolNavigationContext context)
    {
        if (!TryGetBool(context.Payload, "success", out var success)
            || !success
            || !TryGetString(context.Payload, "mode", out var mode)
            || !string.Equals(mode, "start", StringComparison.OrdinalIgnoreCase)
            || !TryGetInt(context.Payload, "requestedDuration", out var requestedDuration)
            || !TryGetInt(context.Payload, "effectiveDuration", out var effectiveDuration)
            || effectiveDuration <= requestedDuration
            || !TryGetBool(context.Payload, "shortDurationOverrideUsed", out var overrideUsed)
            || overrideUsed)
        {
            return Array.Empty<ToolNextStep>();
        }

        var eventName = TryGetString(context.Arguments, "eventName");
        if (eventName is null)
        {
            return Array.Empty<ToolNextStep>();
        }

        var reason = $"The requested {requestedDuration} ms start window was raised to {effectiveDuration} ms by the safe default; retry explicitly to honor the shorter window.";
        return
        [
            new ToolNextStep(
                "trace_routed_events",
                NavigationParamBuilders.Create(
                    ("processId", TryGetInt(context.Arguments, "processId")),
                    ("eventName", eventName),
                    ("elementId", TryGetString(context.Arguments, "elementId")),
                    ("durationMs", requestedDuration),
                    ("mode", "start"),
                    ("allowShortStartDuration", true),
                    ("maxEvents", TryGetInt(context.Arguments, "maxEvents"))),
                reason,
                ToolNextStepKind.Action,
                1,
                ExpectedOutcome: $"Start a trace session that honors the requested {requestedDuration} ms duration.")
            {
                AllowSameToolRetry = true
            }
        ];
    }

    private static IReadOnlyList<ToolNextStep> BuildGetEventHandlers(ToolNavigationContext context)
    {
        if (!TryResolveElementId(context, out var elementId)
            || !TryGetString(context.Payload, "eventName", out var eventName)
            || !string.Equals(eventName, "Click", StringComparison.OrdinalIgnoreCase)
            || !TryGetBool(context.Payload, "mayBeIncomplete", out var mayBeIncomplete)
            || !mayBeIncomplete
            || !TryGetInt(context.Payload, "handlerCount", out var handlerCount)
            || handlerCount != 0)
        {
            return Array.Empty<ToolNextStep>();
        }

        return
        [
            new ToolNextStep(
                "get_commands",
                NavigationParamBuilders.Create(
                    ("processId", TryGetInt(context.Arguments, "processId")),
                    ("elementId", elementId)),
                "Reflection-based Click handler inspection may miss ICommand-backed activation paths; inspect commands on the same element.",
                ToolNextStepKind.Diagnostic,
                1)
        ];
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

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
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

    private static bool TryGetBool(JsonElement element, string propertyName, out bool value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        if (element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static int? TryGetInt(JsonElement? element, string propertyName)
    {
        if (element is { } candidate && TryGetInt(candidate, propertyName, out var value))
        {
            return value;
        }

        return null;
    }

    private static string? TryGetString(JsonElement? element, string propertyName)
    {
        return element is { } candidate && TryGetString(candidate, propertyName, out var value)
            ? value
            : null;
    }
}
