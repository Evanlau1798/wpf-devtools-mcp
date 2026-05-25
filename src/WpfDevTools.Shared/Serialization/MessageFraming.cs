using System.Buffers;
using System.Buffers.Binary;
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
    /// <summary>
    /// Maximum UTF-8 payload size accepted by the length-prefixed IPC frame.
    /// </summary>
    public const int MaxMessageSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int LengthPrefixSize = 4;

    /// <summary>
    /// Write a message to the stream with length-prefix framing
    /// Format: [4 bytes little-endian length][message bytes]
    /// </summary>
    /// <param name="stream">Stream to write to (PipeStream, SslStream, etc.)</param>
    /// <param name="message">Message string to write</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ArgumentNullException">Thrown when stream or message is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when message exceeds maximum size</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled</exception>
    /// <exception cref="IOException">Thrown when write fails</exception>
    public static async Task WriteMessageAsync(
        Stream stream,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (message == null) throw new ArgumentNullException(nameof(message));

        var messageByteCount = Encoding.UTF8.GetByteCount(message);

        if (messageByteCount > MaxMessageSizeBytes)
        {
            throw new InvalidOperationException(
                $"Message size ({messageByteCount} bytes) exceeds maximum allowed size ({MaxMessageSizeBytes} bytes)");
        }

        // Combine length prefix and message into single pooled buffer (zero intermediate allocation)
        var totalLength = LengthPrefixSize + messageByteCount;
        var combined = ArrayPool<byte>.Shared.Rent(totalLength);
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(combined.AsSpan(0, LengthPrefixSize), messageByteCount);
            Encoding.UTF8.GetBytes(message, 0, message.Length, combined, LengthPrefixSize);

#if NET48
            // .NET 4.8: Check cancellation before write
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await stream.WriteAsync(combined, 0, totalLength);
                await stream.FlushAsync();
            }
            catch (IOException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Write operation was cancelled", ex, cancellationToken);
            }
#else
            // .NET 8.0+: Native cancellation token support
            await stream.WriteAsync(combined.AsMemory(0, totalLength), cancellationToken);
            await stream.FlushAsync(cancellationToken);
#endif
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(combined, clearArray: true);
        }
    }

    /// <summary>
    /// Read a message from the stream with length-prefix framing
    /// Uses ArrayPool for buffer reuse to minimize allocations
    /// </summary>
    /// <param name="stream">Stream to read from (PipeStream, SslStream, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Message string read from stream</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null</exception>
    /// <exception cref="EndOfStreamException">Thrown when connection is closed by peer</exception>
    /// <exception cref="InvalidOperationException">Thrown when message length is invalid</exception>
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
            // Read length prefix (4 bytes) - loop for partial reads
            var lengthBytes = new byte[LengthPrefixSize];
            await ReadExactBytesNet48Async(stream, lengthBytes, LengthPrefixSize, cancellationToken);

            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes.AsSpan(0, LengthPrefixSize));

            if (messageLength < 0 || messageLength > MaxMessageSizeBytes)
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
                await ReadExactBytesNet48Async(stream, messageBytes, messageLength, cancellationToken);

                // Only decode the actual message length, not the entire rented buffer
                return Encoding.UTF8.GetString(messageBytes, 0, messageLength);
            }
            finally
            {
                // Return buffer to pool for reuse
                ArrayPool<byte>.Shared.Return(messageBytes, clearArray: true);
            }
        }
        catch (IOException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Read operation was cancelled", ex, cancellationToken);
        }
#else
        // .NET 8.0+: Native cancellation token support
        // Read length prefix (4 bytes) - loop for partial reads
        var lengthBytes = new byte[LengthPrefixSize];
        await ReadExactBytesAsync(stream, lengthBytes, LengthPrefixSize, cancellationToken);

        var messageLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes.AsSpan(0, LengthPrefixSize));

        if (messageLength < 0 || messageLength > MaxMessageSizeBytes)
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
            await ReadExactBytesAsync(stream, messageBytes, messageLength, cancellationToken);

            // Only decode the actual message length, not the entire rented buffer
            return Encoding.UTF8.GetString(messageBytes, 0, messageLength);
        }
        finally
        {
            // Return buffer to pool for reuse
            ArrayPool<byte>.Shared.Return(messageBytes, clearArray: true);
        }
#endif
    }

#if NET48
    /// <summary>
    /// Read exactly the specified number of bytes, looping for partial reads.
    /// Named Pipes and SslStream may return fewer bytes than requested per read.
    /// </summary>
    private static async Task ReadExactBytesNet48Async(
        Stream stream, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = await stream.ReadAsync(buffer, totalRead, count - totalRead);

            if (bytesRead == 0)
            {
                throw new EndOfStreamException(
                    $"Connection closed: received {totalRead} of {count} expected bytes");
            }

            totalRead += bytesRead;
        }
    }
#else
    /// <summary>
    /// Read exactly the specified number of bytes, looping for partial reads.
    /// Named Pipes and SslStream may return fewer bytes than requested per read.
    /// </summary>
    private static async Task ReadExactBytesAsync(
        Stream stream, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(totalRead, count - totalRead),
                cancellationToken);

            if (bytesRead == 0)
            {
                throw new EndOfStreamException(
                    $"Connection closed: received {totalRead} of {count} expected bytes");
            }

            totalRead += bytesRead;
        }
    }
#endif
}
