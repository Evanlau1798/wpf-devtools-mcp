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
    /// JSON schema for tool parameters (JSON Schema Draft 2020-12 format).
    /// Defines the structure and validation rules for tool input parameters.
    /// </summary>
    /// <remarks>
    /// Example schema:
    /// <code>
    /// new {
    ///     type = "object",
    ///     properties = new {
    ///         processId = new {
    ///             type = "integer",
    ///             description = "Process ID of target WPF application"
    ///         },
    ///         elementId = new {
    ///             type = "string",
    ///             description = "Optional element ID to inspect"
    ///         }
    ///     },
    ///     required = new[] { "processId" }
    /// }
    /// </code>
    /// The schema follows JSON Schema Draft 2020-12 specification.
    /// See: https://json-schema.org/draft/2020-12/json-schema-core.html
    /// </remarks>
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
