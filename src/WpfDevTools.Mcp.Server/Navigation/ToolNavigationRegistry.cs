using WpfDevTools.Mcp.Server.Schema;
using WpfDevTools.Mcp.Server.Navigation.Rules;

namespace WpfDevTools.Mcp.Server.Navigation;

public sealed class ToolNavigationRegistry
{
    private readonly Dictionary<string, Func<ToolNavigationContext, ToolNavigationEnvelope>> _handlers =
        new(StringComparer.Ordinal);

    public ToolNavigationRegistry()
    {
        BindingNavigationRules.Register(this);
        SceneDiagnosticNavigationRules.Register(this);
        ActionNavigationRules.Register(this);
        SceneNavigationRules.Register(this);
    }

    public void Register(string toolName, Func<ToolNavigationContext, IReadOnlyList<ToolNextStep>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[toolName] = context => ToolNavigationEnvelope.FromRecommended(handler(context));
    }

    public void Register(string toolName, Func<ToolNavigationContext, ToolNavigationEnvelope> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[toolName] = handler;
    }

    public bool TryResolve(
        string toolName,
        out Func<ToolNavigationContext, ToolNavigationEnvelope>? handler) =>
        _handlers.TryGetValue(toolName, out handler);
}
