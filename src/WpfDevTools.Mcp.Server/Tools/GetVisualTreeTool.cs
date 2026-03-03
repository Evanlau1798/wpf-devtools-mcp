namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get Visual Tree from WPF process
/// </summary>
public class GetVisualTreeTool
{
    private readonly SessionManager _sessionManager;

    public GetVisualTreeTool(SessionManager? sessionManager = null)
    {
        _sessionManager = sessionManager ?? new SessionManager();
    }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<object> ExecuteAsync(object parameters, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Suppress async warning

        // Parse parameters
        int? processId = null;
        int? depth = null;
        string? elementId = null;

        if (parameters != null)
        {
            var paramsType = parameters.GetType();

            var processIdProp = paramsType.GetProperty("processId");
            var processIdValue = processIdProp?.GetValue(parameters);
            if (processIdValue != null)
            {
                processId = Convert.ToInt32(processIdValue);
            }

            var depthProp = paramsType.GetProperty("depth");
            var depthValue = depthProp?.GetValue(parameters);
            if (depthValue != null)
            {
                depth = Convert.ToInt32(depthValue);
            }

            var elementIdProp = paramsType.GetProperty("elementId");
            elementId = elementIdProp?.GetValue(parameters)?.ToString();
        }

        if (!processId.HasValue)
        {
            return new
            {
                success = false,
                error = "Missing required parameter: processId"
            };
        }

        // Check if session exists
        if (!_sessionManager.HasSession(processId.Value))
        {
            return new
            {
                success = false,
                error = $"Process {processId.Value} is not connected"
            };
        }

        // TODO: Implement Named Pipe communication to Inspector
        // For now, return a placeholder response
        return new
        {
            success = true,
            message = "Visual tree retrieval not yet implemented (requires Named Pipe communication)",
            processId = processId.Value,
            depth = depth,
            elementId = elementId
        };
    }
}
