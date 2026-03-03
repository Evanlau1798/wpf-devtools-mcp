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

    public bool IsRunning => _isRunning;

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    public HttpTransport(int port)
    {
        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");
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
            builder.WebHost.UseUrls($"http://localhost:{_port}");

            // Disable logging for tests
            builder.Logging.ClearProviders();

            _app = builder.Build();

            // Configure routes
            _app.MapPost("/mcp", async (HttpContext context) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var message = await reader.ReadToEndAsync();
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
        _app?.DisposeAsync().AsTask().Wait();
    }
}
