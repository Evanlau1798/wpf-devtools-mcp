using System.Diagnostics;
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
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments);
        if (error != null) return error;

        if (!_sessionManager.HasSession(processId))
            return CreateNotConnectedError(processId);

        // Send actual ping through Named Pipe and measure latency
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await SendInspectorRequestAsync(processId, "ping", new { }, cancellationToken);
            stopwatch.Stop();

            // Update last activity
            _sessionManager.UpdateLastActivity(processId);

            return new
            {
                success = true,
                status = "connected",
                processId,
                latencyMs = stopwatch.ElapsedMilliseconds,
                lastActivity = _sessionManager.GetLastActivityTime(processId)
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new
            {
                success = false,
                error = $"Ping failed: {ex.Message}",
                processId
            };
        }
    }
}
