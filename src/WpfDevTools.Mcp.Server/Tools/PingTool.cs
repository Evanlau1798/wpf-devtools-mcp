using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to ping a connected WPF process
/// </summary>
public class PingTool : PipeConnectedToolBase
{
    public PingTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments);
        if (error != null) return Task.FromResult<object>(error);

        if (!_sessionManager.HasSession(processId))
            return Task.FromResult(CreateNotConnectedError(processId));

        // Update last activity
        _sessionManager.UpdateLastActivity(processId);

        return Task.FromResult<object>(new
        {
            success = true,
            status = "connected",
            processId,
            lastActivity = _sessionManager.GetLastActivityTime(processId)
        });
    }
}
