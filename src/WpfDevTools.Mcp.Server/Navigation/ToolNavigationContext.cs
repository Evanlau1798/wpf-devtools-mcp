using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Navigation;

public sealed record ToolNavigationContext(
    string ToolName,
    JsonElement Payload,
    JsonElement? Arguments,
    NavigationSessionState? SessionState = null);
