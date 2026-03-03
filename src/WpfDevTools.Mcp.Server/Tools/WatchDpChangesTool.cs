namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to watch DependencyProperty changes
/// </summary>
public class WatchDpChangesTool
{
    private readonly SessionManager _sessionManager;

    public WatchDpChangesTool(SessionManager? sessionManager = null)
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
        string? propertyName = null;
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

            var propertyNameProp = paramsType.GetProperty("propertyName");
            propertyName = propertyNameProp?.GetValue(parameters)?.ToString();

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

        if (string.IsNullOrEmpty(propertyName))
        {
            return new
            {
                success = false,
                error = "Missing required parameter: propertyName"
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
        // TODO: Implement event push mechanism from Inspector to MCP Server
        // For now, return a placeholder response
        return new
        {
            success = true,
            message = "DependencyProperty change watching not yet implemented (requires Named Pipe communication and event push)",
            processId = processId.Value,
            propertyName = propertyName,
            elementId = elementId
        };
    }
}
