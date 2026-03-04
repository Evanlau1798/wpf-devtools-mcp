using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Utilities;

// Initialize components
var logger = new FileLogger();
var toolRegistry = new ToolRegistry();
var sessionManager = new SessionManager();
var protocolHandler = new McpProtocolHandler(toolRegistry);

logger.LogInfo("WPF DevTools MCP Server starting...");
logger.LogInfo($"Log file: {logger.LogFilePath}");

// Register all tools
ToolRegistrar.RegisterAll(toolRegistry, sessionManager);
logger.LogInfo($"Registered {toolRegistry.GetAllTools().Count} tools");

logger.LogInfo("MCP Server ready. Listening on STDIN...");

// STDIO transport loop
try
{
    using var stdin = Console.OpenStandardInput();
    using var stdout = Console.OpenStandardOutput();
    using var reader = new StreamReader(stdin);
    using var writer = new StreamWriter(stdout) { AutoFlush = true };

    while (!reader.EndOfStream)
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

        logger.LogDebug($"Received: {line}");

        try
        {
            var response = await protocolHandler.HandleRequestAsync(line, CancellationToken.None);
            if (!string.IsNullOrEmpty(response))
            {
                await writer.WriteLineAsync(response);
                logger.LogDebug($"Sent: {response}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Error processing request: {ex.Message}");
        }
    }
}
catch (Exception ex)
{
    logger.LogError($"Fatal error: {ex}");
    return 1;
}

logger.LogInfo("MCP Server shutting down.");
return 0;
