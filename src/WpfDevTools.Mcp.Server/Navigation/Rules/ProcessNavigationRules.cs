using System.Text.Json;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation.Rules;

internal static class ProcessNavigationRules
{
    public static void Register(ToolNavigationRegistry registry)
    {
        registry.Register("connect", BuildConnect);
    }

    private static ToolNavigationEnvelope BuildConnect(ToolNavigationContext context)
    {
        if (TryGetBool(context.Payload, "success", out var success) && success)
        {
            return ToolNavigationEnvelope.FromRecommended(
                [
                    CreateNavigation(
                        "get_ui_summary",
                        BuildConnectedProcessParams(context),
                        "Build the first scene-level summary for the connected WPF process before expanding tree-heavy diagnostics.",
                        1,
                        "The process is connected; scene-first context is now the most useful next read.",
                        ["get_element_snapshot", "get_form_summary"])
                ],
                alternatives:
                [
                    CreateNavigation(
                        "get_form_summary",
                        BuildConnectedProcessParams(context),
                        "Summarize form controls when the next task is interaction or validation focused.",
                        2,
                        "Use this after connect when forms and commands are the immediate target."),
                    CreateNavigation(
                        "get_element_snapshot",
                        BuildConnectedProcessParams(context),
                        "Inspect a focused element after connect when an elementId is already known.",
                        3,
                        "Use this after connect for a known element instead of dumping full trees.",
                        preconditions: ["A runtime elementId is already known from the task or a prior scene summary."])
                ]);
        }

        if (TryGetInt(context.Payload, "candidateCount", out var candidateCount) && candidateCount > 1)
        {
            return ToolNavigationEnvelope.FromRecommended(
                [
                    CreateNavigation(
                        "get_processes",
                        NavigationParamBuilders.Create(
                            ("windowFilter", TryGetString(context.Arguments, "windowFilter"))),
                        "List candidate WPF processes so the next connect call can specify processId explicitly.",
                        1,
                        "connect found multiple candidates and needs disambiguation.")
                ]);
        }

        return ToolNavigationEnvelope.Empty;
    }

    private static JsonElement BuildConnectedProcessParams(ToolNavigationContext context)
    {
        var processId = TryGetInt(context.Payload, "processId")
            ?? TryGetInt(context.Arguments, "processId");
        return NavigationParamBuilders.Create(("processId", processId));
    }

    private static ToolNextStep CreateNavigation(
        string tool,
        JsonElement parameters,
        string reason,
        int priority,
        string whyNow,
        IReadOnlyList<string>? prefetchTools = null,
        IReadOnlyList<string>? preconditions = null) =>
        new(
            tool,
            parameters,
            reason,
            ToolNextStepKind.Navigation,
            priority,
            Preconditions: preconditions,
            PrefetchTools: prefetchTools,
            WhyNow: whyNow);

    private static bool TryGetBool(JsonElement element, string propertyName, out bool value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        value = false;
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

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static string? TryGetString(JsonElement? element, string propertyName)
    {
        if (element is { } candidate
            && candidate.ValueKind == JsonValueKind.Object
            && candidate.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}
