namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to ping a connected WPF process
/// </summary>
public class PingTool
{
    private readonly SessionManager _sessionManager;

    public PingTool(SessionManager? sessionManager = null)
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
        if (parameters != null)
        {
            var paramsType = parameters.GetType();
            var processIdProp = paramsType.GetProperty("processId");
            var processIdValue = processIdProp?.GetValue(parameters);
            if (processIdValue != null)
            {
                processId = Convert.ToInt32(processIdValue);
            }
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

        // Update last activity
        _sessionManager.UpdateLastActivity(processId.Value);

        return new
        {
            success = true,
            status = "connected",
            processId = processId.Value,
            lastActivity = _sessionManager.GetLastActivityTime(processId.Value)
        };
    }
}
