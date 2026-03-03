namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to get triggers from WPF element styles
/// </summary>
public class GetTriggersTool
{
    private readonly SessionManager _sessionManager;

    public GetTriggersTool(SessionManager? sessionManager = null)
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
            message = "Triggers retrieval not yet implemented (requires Named Pipe communication)",
            processId = processId.Value,
            elementId = elementId
        };
    }
}
