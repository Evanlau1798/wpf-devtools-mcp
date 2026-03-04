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

#if NET48
        // .NET 4.8: Use CancellationToken.Register to close pipe on cancellation
        using (cancellationToken.Register(() => pipe.Close()))
        {
            try
            {
                await pipe.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                await pipe.WriteAsync(messageBytes, 0, messageBytes.Length);
                await pipe.FlushAsync();
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
#else
        // .NET 8.0+: Native cancellation token support
        await pipe.WriteAsync(lengthBytes, cancellationToken);
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

#if NET48
        // .NET 4.8: Use CancellationToken.Register to close pipe on cancellation
        using (cancellationToken.Register(() => pipe.Close()))
        {
            try
            {
                // Read length prefix (4 bytes)
                var lengthBytes = new byte[4];
                var bytesRead = await pipe.ReadAsync(lengthBytes, 0, 4);

                if (bytesRead != 4)
                {
                    throw new InvalidOperationException("Failed to read message length");
                }

                var messageLength = BitConverter.ToInt32(lengthBytes, 0);

                if (messageLength < 0 || messageLength > MaxMessageSize)
                {
                    throw new InvalidOperationException(
                        $"Invalid message length: {messageLength}");
                }

                // Handle zero-length messages
                if (messageLength == 0)
                {
                    return string.Empty;
                }

                // Read message
                var messageBytes = new byte[messageLength];
                var totalRead = 0;

                while (totalRead < messageLength)
                {
                    bytesRead = await pipe.ReadAsync(
                        messageBytes,
                        totalRead,
                        messageLength - totalRead);

                    if (bytesRead == 0)
                    {
                        throw new InvalidOperationException("Unexpected end of stream");
                    }

                    totalRead += bytesRead;
                }

                return Encoding.UTF8.GetString(messageBytes);
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
#else
        // .NET 8.0+: Native cancellation token support
        // Read length prefix (4 bytes)
        var lengthBytes = new byte[4];
        var bytesRead = await pipe.ReadAsync(lengthBytes, cancellationToken);

        if (bytesRead != 4)
        {
            throw new InvalidOperationException("Failed to read message length");
        }

        var messageLength = BitConverter.ToInt32(lengthBytes, 0);

        if (messageLength < 0 || messageLength > MaxMessageSize)
        {
            throw new InvalidOperationException(
                $"Invalid message length: {messageLength}");
        }

        // Handle zero-length messages
        if (messageLength == 0)
        {
            return string.Empty;
        }

        // Read message
        var messageBytes = new byte[messageLength];
        var totalRead = 0;

        while (totalRead < messageLength)
        {
            bytesRead = await pipe.ReadAsync(
                messageBytes.AsMemory(totalRead, messageLength - totalRead),
                cancellationToken);

            if (bytesRead == 0)
            {
                throw new InvalidOperationException("Unexpected end of stream");
            }

            totalRead += bytesRead;
        }

        return Encoding.UTF8.GetString(messageBytes);
#endif
    }
}
