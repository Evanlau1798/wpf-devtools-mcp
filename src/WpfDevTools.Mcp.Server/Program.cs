using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Utilities;

[assembly: SupportedOSPlatform("windows")]

// IMPORTANT: STDIO-based MCP servers must NEVER write to stdout.
// All logging goes to stderr (console) and file via FileLoggerProvider.

var fileLogger = new FileLogger();
AuthenticationManager? authManager = null;

try
{
    var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args
    });
    var serverVersion = ServerMetadata.GetDisplayVersion();

    // Logging: stderr for STDIO compatibility + file for persistence
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options =>
        options.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Logging.AddProvider(new FileLoggerProvider(fileLogger));

    // DI: Core services (singletons shared across all tool invocations)
    builder.Services.AddSingleton(fileLogger);
    builder.Services.AddSingleton<IRateLimiterManager>(
        new RateLimiterManager(McpServerConfiguration.RateLimitRequestsPerMinute));
    builder.Services.AddSingleton<MetricsCollector>();

    // Security: Authentication and encryption (enabled when env vars are set)
    // WPFDEVTOOLS_AUTH_SECRET -> base64-encoded shared secret for HMAC-SHA256 challenge-response auth
    // WPFDEVTOOLS_CERT_DIR -> enables TLS encryption over Named Pipes
    var authSecretEnv = Environment.GetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET");
    var certDirEnv = Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR");

    CertificateManager? certManager = null;

    if (!string.IsNullOrWhiteSpace(authSecretEnv))
    {
        authManager = new AuthenticationManager(() => authSecretEnv);
        builder.Services.AddSingleton(authManager);
        fileLogger.LogInfo("Authentication enabled via WPFDEVTOOLS_AUTH_SECRET");
    }

    if (!string.IsNullOrWhiteSpace(certDirEnv))
    {
        certManager = new CertificateManager(certDirEnv);
        builder.Services.AddSingleton(certManager);
        fileLogger.LogInfo($"TLS encryption enabled via WPFDEVTOOLS_CERT_DIR: {certDirEnv}");
    }

    builder.Services.AddSingleton(sp => new SessionManager(
        sp.GetRequiredService<IRateLimiterManager>(),
        authManager,
        certManager));

    // MCP Server configuration
    builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "wpf-devtools-mcp", Version = serverVersion };
        options.ServerInstructions = ServerInstructions.Value;
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

    fileLogger.LogInfo("WPF DevTools MCP Server starting (SDK mode)...");
    fileLogger.LogInfo($"Log file: {fileLogger.LogFilePath}");

    var host = builder.Build();

    // Wire MetricsCollector into the static ToolCallHelper for recording tool execution metrics
    ToolCallHelper.SetMetricsCollector(host.Services.GetRequiredService<MetricsCollector>());

    await host.RunAsync();
}
catch (Exception ex)
{
    fileLogger.LogError($"Fatal error: {ex}");
    throw;
}
finally
{
    fileLogger.LogInfo("WPF DevTools MCP Server shutting down.");

    // Securely zero shared secret from memory on shutdown
    authManager?.Dispose();

    fileLogger.Dispose();
}
