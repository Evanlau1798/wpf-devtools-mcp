using System.Diagnostics;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Utilities;

// IMPORTANT: STDIO-based MCP servers must NEVER write to stdout (Console.WriteLine).
// All logging goes to file via FileLogger. See: https://modelcontextprotocol.io/docs/develop/build-server#c

// Initialize components
using var logger = new FileLogger();
var toolRegistry = new ToolRegistry();
using var sessionManager = new SessionManager();
var protocolHandler = new McpProtocolHandler(toolRegistry);
var metrics = new MetricsCollector();

// SECURITY: Global rate limiter to prevent STDIN message flooding
var globalRateLimiter = new RateLimiter(
    maxRequestsPerInterval: 100,
    interval: TimeSpan.FromMinutes(1));

logger.LogInfo("WPF DevTools MCP Server starting...");
logger.LogInfo($"Log file: {logger.LogFilePath}");

// Register all tools
ToolRegistrar.RegisterAll(toolRegistry, sessionManager);
logger.LogInfo($"Registered {toolRegistry.GetAllTools().Count} tools");

logger.LogInfo("MCP Server ready. Listening on STDIN...");

// Graceful shutdown via cancellation token
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true; // Prevent immediate termination
    cts.Cancel();
    logger.LogInfo("Shutdown requested (CTRL+C)...");
};

// STDIO transport loop
try
{
    using var stdin = Console.OpenStandardInput();
    using var stdout = Console.OpenStandardOutput();
    using var reader = new StreamReader(stdin);
    using var writer = new StreamWriter(stdout) { AutoFlush = true };

    while (!reader.EndOfStream && !cts.Token.IsCancellationRequested)
    {
        var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
        if (line == null) break;
        if (line.Length > 1_048_576) // 1 MB limit
        {
            logger.LogWarning("Received oversized message, skipping");
            continue;
        }
        if (string.IsNullOrWhiteSpace(line))
            continue;

        // SECURITY: Check global rate limit to prevent DoS
        if (!globalRateLimiter.TryAcquire())
        {
            logger.LogWarning($"Global rate limit exceeded. Available tokens: {globalRateLimiter.GetAvailableTokens()}");

            var errorResponse = System.Text.Json.JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = (object?)null,
                error = new
                {
                    code = -32000,
                    message = "Rate limit exceeded. Maximum 100 requests per minute. Please slow down."
                }
            });
            await writer.WriteLineAsync(errorResponse).ConfigureAwait(false);
            continue;
        }

        logger.LogDebug($"Received: ({line.Length} chars)");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await protocolHandler.HandleRequestAsync(line, cts.Token).ConfigureAwait(false);
            stopwatch.Stop();

            if (!string.IsNullOrEmpty(response))
            {
                await writer.WriteLineAsync(response).ConfigureAwait(false);
                logger.LogDebug($"Sent: ({response.Length} chars, {stopwatch.ElapsedMilliseconds}ms)");
                metrics.RecordRequest("request", stopwatch.ElapsedMilliseconds, success: true);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInfo("Request cancelled due to shutdown");
            break;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            metrics.RecordRequest("request", stopwatch.ElapsedMilliseconds, success: false);
            logger.LogError($"Error processing request: {ex.Message}");
        }
    }
}
catch (OperationCanceledException)
{
    logger.LogInfo("Shutdown completed gracefully");
}
catch (Exception ex)
{
    logger.LogError($"Fatal error: {ex}");
    return 1;
}

// Log final metrics summary
var snapshot = metrics.GetSnapshot();
logger.LogInfo($"MCP Server shutting down. Total requests: {snapshot.TotalRequests}, " +
    $"Success: {snapshot.SuccessCount}, Errors: {snapshot.ErrorCount}, " +
    $"Avg latency: {snapshot.AverageLatency:F1}ms, P95: {snapshot.P95Latency:F1}ms");
return 0;
