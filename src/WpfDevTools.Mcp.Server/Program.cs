using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Utilities;

// IMPORTANT: STDIO-based MCP servers must NEVER write to stdout.
// All logging goes to stderr (console) and file via FileLoggerProvider.

var fileLogger = new FileLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Logging: stderr for STDIO compatibility + file for persistence
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options =>
        options.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Logging.AddProvider(new FileLoggerProvider(fileLogger));

    // DI: Core services (singletons shared across all tool invocations)
    builder.Services.AddSingleton(fileLogger);
    builder.Services.AddSingleton<SessionManager>();
    builder.Services.AddSingleton(new RateLimiter(
        maxRequestsPerInterval: 100,
        interval: TimeSpan.FromMinutes(1)));
    builder.Services.AddSingleton<MetricsCollector>();

    // MCP Server configuration
    builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "wpf-devtools-mcp", Version = "0.1.0" };
        options.ServerInstructions = ServerInstructions.Value;
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

    fileLogger.LogInfo("WPF DevTools MCP Server starting (SDK mode)...");
    fileLogger.LogInfo($"Log file: {fileLogger.LogFilePath}");

    await builder.Build().RunAsync();
}
catch (Exception ex)
{
    fileLogger.LogError($"Fatal error: {ex}");
    throw;
}
finally
{
    fileLogger.LogInfo("WPF DevTools MCP Server shutting down.");
    fileLogger.Dispose();
}
