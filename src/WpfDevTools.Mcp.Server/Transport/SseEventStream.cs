using System.Text;
using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Transport;

/// <summary>
/// Server-Sent Events (SSE) event stream
/// </summary>
public class SseEventStream : IDisposable
{
    private readonly Stream _stream;
    private readonly StreamWriter _writer;

    public SseEventStream(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true);
    }

    /// <summary>
    /// Send an event to the client
    /// </summary>
    public async Task SendEventAsync(string? eventName, object data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Write event name if provided (sanitize to prevent SSE injection)
        if (!string.IsNullOrEmpty(eventName))
        {
            var safeName = eventName.Replace("\n", "").Replace("\r", "");
            await _writer.WriteLineAsync($"event: {safeName}");
        }

        // Serialize data to JSON
        var jsonData = JsonSerializer.Serialize(data);

        // Write data (can be multi-line)
        var lines = jsonData.Split('\n');
        foreach (var line in lines)
        {
            await _writer.WriteLineAsync($"data: {line}");
        }

        // Write empty line to signal end of event
        await _writer.WriteLineAsync();
        await _writer.FlushAsync();
    }

    public void Dispose()
    {
        _writer?.Dispose();
    }
}
