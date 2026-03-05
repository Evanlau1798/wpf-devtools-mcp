using System.Buffers;
using System.IO.Pipes;
using System.Text;

namespace WpfDevTools.Shared.Serialization;

/// <summary>
/// Message framing utilities for Named Pipes communication
/// Uses length-prefix framing to handle message boundaries
/// Optimized with ArrayPool for buffer reuse to minimize allocations
/// </summary>
public static class MessageFraming
{
    private const int MaxMessageSize = 10 * 1024 * 1024; // 10 MB
    private const int MinMessageSize = 1; // Minimum 1 byte to prevent zero-length allocation attacks

    /// <summary>
    /// Write a message to the pipe with length-prefix framing
    /// Format: [4 bytes length][message bytes]
    /// </summary>
    /// <param name="pipe">Pipe stream to write to</param>
    /// <param name="message">Message string to write</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ArgumentNullException">Thrown when pipe or message is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled</exception>
    /// <exception cref="IOException">Thrown when pipe write fails</exception>
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
        // .NET 4.8: Check cancellation before each operation to avoid partial writes
        // Complete atomic message writes (length + message) to maintain protocol integrity
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await pipe.WriteAsync(lengthBytes, 0, lengthBytes.Length);

            cancellationToken.ThrowIfCancellationRequested();
            await pipe.WriteAsync(messageBytes, 0, messageBytes.Length);

            cancellationToken.ThrowIfCancellationRequested();
            await pipe.FlushAsync();
        }
        catch (IOException ex) when (cancellationToken.IsCancellationRequested)
        {
            // If cancellation caused the IOException, wrap it
            throw new OperationCanceledException("Write operation was cancelled", ex, cancellationToken);
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
    /// Uses ArrayPool for buffer reuse to minimize allocations
    /// </summary>
    /// <param name="pipe">Pipe stream to read from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Message string read from pipe</returns>
    /// <exception cref="ArgumentNullException">Thrown when pipe is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when message length is invalid or stream ends unexpectedly</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled</exception>
    /// <exception cref="IOException">Thrown when pipe read fails</exception>
    public static async Task<string> ReadMessageAsync(
        PipeStream pipe,
        CancellationToken cancellationToken = default)
    {
        if (pipe == null) throw new ArgumentNullException(nameof(pipe));

#if NET48
        // .NET 4.8: Check cancellation before each operation
        try
        {
            // Read length prefix (4 bytes)
            var lengthBytes = new byte[4];
            cancellationToken.ThrowIfCancellationRequested();
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

            // Rent buffer from ArrayPool instead of allocating
            var messageBytes = ArrayPool<byte>.Shared.Rent(messageLength);
            try
            {
                var totalRead = 0;

                // Loop is required because Named Pipes may return partial reads
                while (totalRead < messageLength)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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

        // Rent buffer from ArrayPool instead of allocating
        var messageBytes = ArrayPool<byte>.Shared.Rent(messageLength);
        try
        {
            var totalRead = 0;

            // Loop is required because Named Pipes may return partial reads
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
