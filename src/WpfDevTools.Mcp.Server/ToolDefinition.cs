using System.Text.Json;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Defines an MCP tool with its metadata
/// </summary>
public class ToolDefinition
{
    /// <summary>
    /// Tool name (unique identifier)
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Human-readable description
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// JSON schema for tool parameters
    /// </summary>
    public required object Parameters { get; init; }

    /// <summary>
    /// Example usage scenarios to help AI agents understand how to use the tool
    /// </summary>
    public object[]? Examples { get; init; }

    /// <summary>
    /// Handler delegate for executing the tool
    /// </summary>
    public Func<JsonElement?, CancellationToken, Task<object>>? ExecuteHandler { get; init; }
}
