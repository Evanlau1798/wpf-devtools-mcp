using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpPrompts;
using WpfDevTools.Mcp.Server.McpResources;
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
        new RateLimiterManager(McpServerConfiguration.GetConfiguredRateLimitRequestsPerMinute()));
    builder.Services.AddSingleton<MetricsCollector>();

    // Security: injection-based inspector sessions are hardened by default.
    // WPFDEVTOOLS_AUTH_SECRET overrides the generated shared secret when a deterministic value is required.
    // WPFDEVTOOLS_CERT_DIR overrides the default certificate directory when certificate storage must be pinned.
    var authSecretEnv = Environment.GetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET");
    var certDirEnv = Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR");

    var transportSecurity = TransportSecurityConfiguration.Create(authSecretEnv, certDirEnv);
    authManager = transportSecurity.AuthenticationManager;
    authSecretEnv = null; // Clear secret from local scope to reduce memory residue

    builder.Services.AddSingleton(authManager);
    builder.Services.AddSingleton(transportSecurity.CertificateManager);
    fileLogger.LogInfo(transportSecurity.GetAuthenticationLogMessage());
    fileLogger.LogInfo(transportSecurity.GetEncryptionLogMessage());

    builder.Services.AddSingleton(sp => new SessionManager(
        sp.GetRequiredService<IRateLimiterManager>(),
        authManager,
        transportSecurity.CertificateManager,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<SessionManager>()));

    // MCP Server configuration
    builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "wpf-devtools-mcp", Version = serverVersion };
        options.ServerInstructions = ServerInstructions.Value;
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly(typeof(WorkflowPrompts).Assembly)
    .WithResourcesFromAssembly(typeof(CapabilityResources).Assembly);

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
