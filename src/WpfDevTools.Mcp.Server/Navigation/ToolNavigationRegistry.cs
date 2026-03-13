using WpfDevTools.Mcp.Server.Schema;
using WpfDevTools.Mcp.Server.Navigation.Rules;

namespace WpfDevTools.Mcp.Server.Navigation;

public sealed class ToolNavigationRegistry
{
    private readonly Dictionary<string, Func<ToolNavigationContext, IReadOnlyList<ToolNextStep>>> _handlers =
        new(StringComparer.Ordinal);

    public ToolNavigationRegistry()
    {
        BindingNavigationRules.Register(this);
        SceneDiagnosticNavigationRules.Register(this);
    }

    public void Register(string toolName, Func<ToolNavigationContext, IReadOnlyList<ToolNextStep>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[toolName] = handler;
    }

    public bool TryResolve(
        string toolName,
        out Func<ToolNavigationContext, IReadOnlyList<ToolNextStep>>? handler) =>
        _handlers.TryGetValue(toolName, out handler);
}
