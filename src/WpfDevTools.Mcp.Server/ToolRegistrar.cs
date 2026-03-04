using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Registers all MCP tools into the tool registry
/// </summary>
public static partial class ToolRegistrar
{
    /// <summary>
    /// Register all 44 MCP tools across all 10 categories
    /// </summary>
    public static void RegisterAll(ToolRegistry registry, SessionManager sessionManager)
    {
        RegisterProcessTools(registry, sessionManager);
        RegisterTreeTools(registry, sessionManager);
        RegisterBindingTools(registry, sessionManager);
        RegisterDependencyPropertyTools(registry, sessionManager);
        RegisterStyleTools(registry, sessionManager);
        RegisterEventTools(registry, sessionManager);
        RegisterInteractionTools(registry, sessionManager);
        RegisterLayoutTools(registry, sessionManager);
        RegisterMvvmTools(registry, sessionManager);
        RegisterPerformanceTools(registry, sessionManager);
    }

    /// <summary>
    /// Helper method to register a tool with optional examples
    /// </summary>
    private static void RegisterTool(
        ToolRegistry registry,
        string name,
        string description,
        object schema,
        Func<JsonElement?, CancellationToken, Task<object>> handler,
        object[]? examples = null)
    {
        registry.RegisterTool(new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = schema,
            Examples = examples,
            ExecuteHandler = handler
        });
    }
}
