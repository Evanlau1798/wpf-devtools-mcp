using System.IO.Pipes;
using System.Text;

namespace WpfDevTools.Shared.Serialization;

/// <summary>
/// Message framing utilities for Named Pipes communication
/// Uses length-prefix framing to handle message boundaries
/// </summary>
public static class MessageFraming
{
    private const int MaxMessageSize = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Write a message to the pipe with length-prefix framing
    /// Format: [4 bytes length][message bytes]
    /// </summary>
    public static async Task WriteMessageAsync(
        PipeStream pipe,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (pipe == null) throw new ArgumentNullException(nameof(pipe));
        if (message == null) throw new ArgumentNullException(nameof(message));

        var messageBytes = Encoding.UTF8.GetBytes(message);
        var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

        // Write length prefix
#if NET48
        await pipe.WriteAsync(lengthBytes, 0, lengthBytes.Length);
#else
        await pipe.WriteAsync(lengthBytes, cancellationToken);
#endif

        // Write message
#if NET48
        await pipe.WriteAsync(messageBytes, 0, messageBytes.Length);
        await pipe.FlushAsync();
#else
        await pipe.WriteAsync(messageBytes, cancellationToken);
        await pipe.FlushAsync(cancellationToken);
#endif
    }

    /// <summary>
    /// Read a message from the pipe with length-prefix framing
    /// </summary>
    public static async Task<string> ReadMessageAsync(
        PipeStream pipe,
        CancellationToken cancellationToken = default)
    {
        if (pipe == null) throw new ArgumentNullException(nameof(pipe));

        // Read length prefix (4 bytes)
        var lengthBytes = new byte[4];
#if NET48
        var bytesRead = await pipe.ReadAsync(lengthBytes, 0, 4);
#else
        var bytesRead = await pipe.ReadAsync(lengthBytes, cancellationToken);
#endif

        if (bytesRead != 4)
        {
            throw new InvalidOperationException("Failed to read message length");
        }

        var messageLength = BitConverter.ToInt32(lengthBytes, 0);

        if (messageLength <= 0 || messageLength > MaxMessageSize)
        {
            throw new InvalidOperationException(
                $"Invalid message length: {messageLength}");
        }

        // Read message
        var messageBytes = new byte[messageLength];
        var totalRead = 0;

        while (totalRead < messageLength)
        {
#if NET48
            bytesRead = await pipe.ReadAsync(
                messageBytes,
                totalRead,
                messageLength - totalRead);
#else
            bytesRead = await pipe.ReadAsync(
                messageBytes.AsMemory(totalRead, messageLength - totalRead),
                cancellationToken);
#endif

            if (bytesRead == 0)
            {
                throw new InvalidOperationException("Unexpected end of stream");
            }

            totalRead += bytesRead;
        }

        return Encoding.UTF8.GetString(messageBytes);
    }
}
