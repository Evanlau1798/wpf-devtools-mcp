namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Registry for MCP tools
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _tools = new();
    private readonly List<ToolDefinition> _toolsInOrder = new();
    private readonly object _lock = new();

    /// <summary>
    /// Register a new tool
    /// </summary>
    public void RegisterTool(ToolDefinition tool)
    {
        if (tool == null)
            throw new ArgumentNullException(nameof(tool));

        lock (_lock)
        {
            if (_tools.ContainsKey(tool.Name))
            {
                throw new InvalidOperationException($"Tool '{tool.Name}' is already registered");
            }

            _tools[tool.Name] = tool;
            _toolsInOrder.Add(tool);
        }
    }

    /// <summary>
    /// Get tool by name
    /// </summary>
    public ToolDefinition? GetTool(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        lock (_lock)
        {
            return _tools.TryGetValue(name, out var tool) ? tool : null;
        }
    }

    /// <summary>
    /// Get all registered tools in registration order
    /// </summary>
    public IReadOnlyList<ToolDefinition> GetAllTools()
    {
        lock (_lock)
        {
            return _toolsInOrder.ToList();
        }
    }
}
