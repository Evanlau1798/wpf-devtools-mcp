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

    public bool IsRunning => _isRunning;

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    public HttpTransport(int port)
    {
        // Port 0 means auto-assign an available port
        if (port < 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 0 and 65535");
        }

        _port = port;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("Transport is already running");
        }

        try
        {
            var builder = WebApplication.CreateBuilder();

            // Use 127.0.0.1 instead of localhost for port 0 support
            var host = _port == 0 ? "127.0.0.1" : "localhost";
            builder.WebHost.UseUrls($"http://{host}:{_port}");

            // Disable logging for tests
            builder.Logging.ClearProviders();

            _app = builder.Build();

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

    public async Task StopAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            _isRunning = false;
        }
    }

    public void Dispose()
    {
        if (_app != null)
        {
            Task.Run(() => _app.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));
        }
    }
}
