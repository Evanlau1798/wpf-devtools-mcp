using WpfDevTools.Mcp.Server;

// Initialize components
var logger = new FileLogger();
var toolRegistry = new ToolRegistry();
var protocolHandler = new McpProtocolHandler(toolRegistry);

logger.LogInfo("WPF DevTools MCP Server starting...");
logger.LogInfo($"Log file: {logger.LogFilePath}");

// Register core tools (Phase 1 MVP - will expand in later tasks)
RegisterCoreTools(toolRegistry, logger);

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
        if (string.IsNullOrWhiteSpace(line))
            continue;

        logger.LogDebug($"Received: {line}");

        try
        {
            var response = await protocolHandler.HandleRequestAsync(line, CancellationToken.None);
            await writer.WriteLineAsync(response);
            logger.LogDebug($"Sent: {response}");
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

static void RegisterCoreTools(ToolRegistry registry, FileLogger logger)
{
    // Phase 1 Week 5: Will implement get_processes, connect, ping tools
    // For now, register placeholder tools for testing
    logger.LogInfo("Registering core tools...");

    // Tools will be registered in Week 5 Task 5.1
}

