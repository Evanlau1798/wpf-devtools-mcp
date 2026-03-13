using System.Diagnostics;
using System.Text.Json;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to ping a connected WPF process
/// </summary>
public sealed class PingTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the PingTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public PingTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the ping tool to check connection status and measure latency
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing connection status and latency or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;

        if (!_sessionManager.HasSession(processId))
            return CreateNotConnectedError(processId);

        // Send actual ping through Named Pipe and measure latency
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await SendInspectorRequestAsync(processId, "ping", new { }, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var responseJson = JsonSerializer.SerializeToElement(response);
            if (ToolCallHelper.IsToolResultError(responseJson))
            {
                return response;
            }

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
            var (errorCode, message) = ToolCallHelper.ClassifyException(ex);
            return new ToolErrorPayload
            {
                Error = message,
                ErrorCode = errorCode,
                Hint = $"Retry ping after reconnecting process {processId}, or inspect server logs if the connection should still be alive."
            };
        }
    }
}
