using System.Buffers;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace WpfDevTools.Shared.Serialization;

/// <summary>
/// Message framing utilities for stream communication (Named Pipes, SslStream, etc.)
/// Uses length-prefix framing to handle message boundaries
/// Optimized with ArrayPool for buffer reuse to minimize allocations
/// </summary>
public static class MessageFraming
{
    private const int MaxMessageSize = 10 * 1024 * 1024; // 10 MB
    private const int MinMessageSize = 1; // Minimum 1 byte to prevent zero-length allocation attacks

    /// <summary>
    /// Write a message to the stream with length-prefix framing
    /// Format: [4 bytes length][message bytes]
    /// </summary>
    /// <param name="stream">Stream to write to (PipeStream, SslStream, etc.)</param>
    /// <param name="message">Message string to write</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ArgumentNullException">Thrown when stream or message is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled</exception>
    /// <exception cref="IOException">Thrown when write fails</exception>
    public static async Task WriteMessageAsync(
        Stream stream,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (message == null) throw new ArgumentNullException(nameof(message));

        var messageBytes = Encoding.UTF8.GetBytes(message);
        var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

        // Combine length prefix and message into single buffer for atomic write
        var combined = new byte[4 + messageBytes.Length];
        Buffer.BlockCopy(lengthBytes, 0, combined, 0, 4);
        Buffer.BlockCopy(messageBytes, 0, combined, 4, messageBytes.Length);

#if NET48
        // .NET 4.8: Check cancellation before write
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await stream.WriteAsync(combined, 0, combined.Length);
            await stream.FlushAsync();
        }
        catch (IOException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Write operation was cancelled", ex, cancellationToken);
        }
#else
        // .NET 8.0+: Native cancellation token support
        await stream.WriteAsync(combined, cancellationToken);
        await stream.FlushAsync(cancellationToken);
#endif
    }

    /// <summary>
    /// Read a message from the stream with length-prefix framing
    /// Uses ArrayPool for buffer reuse to minimize allocations
    /// </summary>
    /// <param name="stream">Stream to read from (PipeStream, SslStream, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Message string read from stream</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when message length is invalid or stream ends unexpectedly</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled</exception>
    /// <exception cref="IOException">Thrown when read fails</exception>
    public static async Task<string> ReadMessageAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));

#if NET48
        // .NET 4.8: Check cancellation before each operation
        try
        {
            // Read length prefix (4 bytes)
            var lengthBytes = new byte[4];
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = await stream.ReadAsync(lengthBytes, 0, 4);

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

            // Rent buffer from ArrayPool instead of allocating
            var messageBytes = ArrayPool<byte>.Shared.Rent(messageLength);
            try
            {
                var totalRead = 0;

                // Loop is required because Named Pipes may return partial reads
                while (totalRead < messageLength)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bytesRead = await stream.ReadAsync(
                        messageBytes,
                        totalRead,
                        messageLength - totalRead);

                    if (bytesRead == 0)
                    {
                        throw new InvalidOperationException("Unexpected end of stream");
                    }

                    totalRead += bytesRead;
                }

                // Only decode the actual message length, not the entire rented buffer
                return Encoding.UTF8.GetString(messageBytes, 0, messageLength);
            }
            finally
            {
                // Return buffer to pool for reuse
                ArrayPool<byte>.Shared.Return(messageBytes);
            }
        }
        catch (IOException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Read operation was cancelled", ex, cancellationToken);
        }
#else
        // .NET 8.0+: Native cancellation token support
        // Read length prefix (4 bytes)
        var lengthBytes = new byte[4];
        var bytesRead = await stream.ReadAsync(lengthBytes, cancellationToken);

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

        // Rent buffer from ArrayPool instead of allocating
        var messageBytes = ArrayPool<byte>.Shared.Rent(messageLength);
        try
        {
            var totalRead = 0;

            // Loop is required because Named Pipes may return partial reads
            while (totalRead < messageLength)
            {
                bytesRead = await stream.ReadAsync(
                    messageBytes.AsMemory(totalRead, messageLength - totalRead),
                    cancellationToken);

                if (bytesRead == 0)
                {
                    throw new InvalidOperationException("Unexpected end of stream");
                }

                totalRead += bytesRead;
            }

            // Only decode the actual message length, not the entire rented buffer
            return Encoding.UTF8.GetString(messageBytes, 0, messageLength);
        }
        finally
        {
            // Return buffer to pool for reuse
            ArrayPool<byte>.Shared.Return(messageBytes);
        }
#endif
    }
}
