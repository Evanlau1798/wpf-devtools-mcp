using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation;

public sealed class ToolNavigationPlanner(ToolNavigationRegistry registry)
{
    private readonly ToolNavigationRegistry _registry = registry;

    public IReadOnlyList<ToolNextStep> Plan(string toolName, System.Text.Json.JsonElement payload, System.Text.Json.JsonElement? arguments)
    {
        if (!_registry.TryResolve(toolName, out var handler) || handler is null)
        {
            return Array.Empty<ToolNextStep>();
        }

        return handler(new ToolNavigationContext(toolName, payload, arguments))
            .OrderBy(step => step.Priority)
            .Take(3)
            .ToArray();
    }
}
