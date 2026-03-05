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

    /// <summary>
    /// Initializes a new instance of the SseEventStream class
    /// </summary>
    /// <param name="stream">The underlying stream to write SSE events to</param>
    /// <exception cref="ArgumentNullException">Thrown when stream is null</exception>
    public SseEventStream(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true);
    }

    /// <summary>
    /// Send an event to the client
    /// </summary>
    /// <param name="eventName">Optional event name (sanitized to prevent SSE injection)</param>
    /// <param name="data">Data object to serialize as JSON</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Task representing the async operation</returns>
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

    /// <summary>
    /// Dispose the event stream and release resources
    /// </summary>
    public void Dispose()
    {
        _writer?.Dispose();
    }
}
