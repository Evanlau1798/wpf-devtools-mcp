using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation;

public sealed class ToolNavigationPlanner(ToolNavigationRegistry registry)
{
    private readonly ToolNavigationRegistry _registry = registry;

    public ToolNavigationEnvelope PlanEnvelope(
        string toolName,
        System.Text.Json.JsonElement payload,
        System.Text.Json.JsonElement? arguments,
        NavigationSessionState? sessionState = null)
    {
        if (!_registry.TryResolve(toolName, out var handler) || handler is null)
        {
            return ToolNavigationEnvelope.Empty;
        }

        return Normalize(toolName, handler(new ToolNavigationContext(toolName, payload, arguments, sessionState)));
    }

    public IReadOnlyList<ToolNextStep> Plan(
        string toolName,
        System.Text.Json.JsonElement payload,
        System.Text.Json.JsonElement? arguments,
        NavigationSessionState? sessionState = null) =>
        PlanEnvelope(toolName, payload, arguments, sessionState).Recommended;

    private static ToolNavigationEnvelope Normalize(string toolName, ToolNavigationEnvelope envelope)
    {
        var recommended = NormalizeSteps(toolName, envelope.Recommended);
        var alternatives = NormalizeSteps(toolName, envelope.Alternatives, recommended);
        var prefetchTools = envelope.PrefetchTools
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ToolNavigationEnvelope(
            recommended,
            alternatives,
            prefetchTools,
            envelope.ContextRefs.Where(reference => reference is not null).ToArray()!);
    }

    private static IReadOnlyList<ToolNextStep> NormalizeSteps(
        string toolName,
        IReadOnlyList<ToolNextStep> steps,
        IReadOnlyList<ToolNextStep>? excluded = null) =>
        steps
            .Where(step => !string.Equals(step.Tool, toolName, StringComparison.Ordinal))
            .Where(step => excluded is null || !excluded.Any(existing => existing.Tool == step.Tool && existing.Params.GetRawText() == step.Params.GetRawText()))
            .OrderBy(step => step.Priority)
            .Take(3)
            .ToArray();
}
