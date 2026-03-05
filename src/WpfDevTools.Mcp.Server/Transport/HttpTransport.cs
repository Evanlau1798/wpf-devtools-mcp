using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WpfDevTools.Mcp.Server.Transport;

/// <summary>
/// HTTP+SSE transport for MCP protocol
/// </summary>
public class HttpTransport : ITransport, IDisposable
{
    private readonly int _port;
    private WebApplication? _app;
    private bool _isRunning;
    private const int MaxBodySizeBytes = 1 * 1024 * 1024; // 1 MB

    /// <summary>
    /// Gets whether the transport is running
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Event raised when a message is received
    /// </summary>
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// Initializes a new instance of the HttpTransport class
    /// </summary>
    /// <param name="port">Port number to listen on (0 for auto-assign, 1-65535 for specific port)</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when port is not between 0 and 65535</exception>
    public HttpTransport(int port)
    {
        // Port 0 means auto-assign an available port
        if (port < 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 0 and 65535");
        }

        _port = port;
    }

    /// <summary>
    /// Start the HTTP transport server
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when transport is already running or port is in use</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("Transport is already running");
        }

        try
        {
            var builder = WebApplication.CreateBuilder();

            // SECURITY: Always bind to 127.0.0.1 to prevent external access
            builder.WebHost.UseUrls($"http://127.0.0.1:{_port}");

            // SECURITY: Enforce request body size limit at Kestrel level
            // This prevents chunked transfer encoding bypass of Content-Length check
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = MaxBodySizeBytes;
            });

            // Disable logging for tests
            builder.Logging.ClearProviders();

            _app = builder.Build();

            // Optional bearer token authentication.
            // Set the WPF_DEVTOOLS_AUTH_TOKEN environment variable to require token auth on all endpoints.
            _app.Use(async (context, next) =>
            {
                var expectedToken = Environment.GetEnvironmentVariable("WPF_DEVTOOLS_AUTH_TOKEN");
                if (!string.IsNullOrEmpty(expectedToken))
                {
                    var actualHeader = (string?)context.Request.Headers["Authorization"] ?? "";
                    var expectedHeader = $"Bearer {expectedToken}";
                    // SECURITY: Use constant-time comparison to prevent timing attacks
                    var isValid = CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(actualHeader),
                        Encoding.UTF8.GetBytes(expectedHeader));
                    if (!context.Request.Headers.TryGetValue("Authorization", out _) || !isValid)
                    {
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsync("Unauthorized. Set WPF_DEVTOOLS_AUTH_TOKEN environment variable and pass as Bearer token.");
                        return;
                    }
                }
                await next(context);
            });

            // Configure routes
            _app.MapPost("/mcp", async (HttpContext context) =>
            {
                // Check Content-Length header to prevent DoS
                if (context.Request.ContentLength.HasValue &&
                    context.Request.ContentLength.Value > MaxBodySizeBytes)
                {
                    context.Response.StatusCode = 413; // Payload Too Large
                    await context.Response.WriteAsync("Request body too large");
                    return;
                }

                using var reader = new StreamReader(context.Request.Body);
                var message = await reader.ReadToEndAsync();

                // Additional check after reading (in case Content-Length was not set)
                if (message.Length > MaxBodySizeBytes)
                {
                    context.Response.StatusCode = 413;
                    await context.Response.WriteAsync("Request body too large");
                    return;
                }

                MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message));

                await context.Response.WriteAsJsonAsync(new { success = true });
            });

            _app.MapGet("/events", async (HttpContext context) =>
            {
                context.Response.Headers.Append("Content-Type", "text/event-stream");
                context.Response.Headers.Append("Cache-Control", "no-cache");
                context.Response.Headers.Append("Connection", "keep-alive");

                // Keep connection open for SSE
                await context.Response.Body.FlushAsync();

                // Wait for cancellation
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
            });

            await _app.StartAsync(cancellationToken);
            _isRunning = true;
        }
        catch (IOException ex) when (ex.Message.Contains("address already in use"))
        {
            throw new InvalidOperationException($"Port {_port} is already in use", ex);
        }
    }

    /// <summary>
    /// Stop the HTTP transport server
    /// </summary>
    /// <returns>Task representing the async operation</returns>
    public async Task StopAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            _isRunning = false;
        }
    }

    /// <summary>
    /// Dispose the HTTP transport and release resources
    /// </summary>
    public void Dispose()
    {
        if (_app != null)
        {
            // CRITICAL FIX: Use ConfigureAwait(false) to prevent deadlock
            _app.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            _isRunning = false;
        }
    }
}
