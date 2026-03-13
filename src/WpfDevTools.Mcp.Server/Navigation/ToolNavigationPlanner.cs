using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation;

public sealed class ToolNavigationPlanner(ToolNavigationRegistry registry)
{
    private readonly ToolNavigationRegistry _registry = registry;

    public IReadOnlyList<ToolNextStep> Plan(
        string toolName,
        System.Text.Json.JsonElement payload,
        System.Text.Json.JsonElement? arguments,
        NavigationSessionState? sessionState = null)
    {
        if (!_registry.TryResolve(toolName, out var handler) || handler is null)
        {
            return Array.Empty<ToolNextStep>();
        }

        return handler(new ToolNavigationContext(toolName, payload, arguments, sessionState))
            .Where(step => !string.Equals(step.Tool, toolName, StringComparison.Ordinal))
            .OrderBy(step => step.Priority)
            .Take(3)
            .ToArray();
    }
}
