using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Utilities;

// Initialize components
var logger = new FileLogger();
var toolRegistry = new ToolRegistry();
using var sessionManager = new SessionManager();
var protocolHandler = new McpProtocolHandler(toolRegistry);

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

// CRITICAL FIX: Add cancellation token for graceful shutdown
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
        var line = await reader.ReadLineAsync();
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

            // Send error response for rate limit
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
            await writer.WriteLineAsync(errorResponse);
            continue;
        }

        logger.LogDebug($"Received: ({line.Length} chars)");

        try
        {
            var response = await protocolHandler.HandleRequestAsync(line, cts.Token);
            if (!string.IsNullOrEmpty(response))
            {
                await writer.WriteLineAsync(response);
                logger.LogDebug($"Sent: ({response.Length} chars)");
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInfo("Request cancelled due to shutdown");
            break;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error processing request: {ex.Message}");
        }
    }
}
catch (OperationCanceledException)
{
    logger.LogInfo("Shutdown completed gracefully");
    return 0;
}
catch (Exception ex)
{
    logger.LogError($"Fatal error: {ex}");
    return 1;
}

logger.LogInfo("MCP Server shutting down.");
return 0;
