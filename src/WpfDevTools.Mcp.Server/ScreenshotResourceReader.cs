using System.Security.Cryptography;

namespace WpfDevTools.Mcp.Server;

internal sealed class ScreenshotResourceReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly object _lock = new();
    private bool _disposed;

    private ScreenshotResourceReader(FileStream stream, int length, string sha256)
    {
        _stream = stream;
        Length = length;
        Sha256 = sha256;
    }

    internal int Length { get; }

    internal string Sha256 { get; }

    internal static ScreenshotResourceReader OpenVerified(string filePath, string expectedSha256)
    {
        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.RandomAccess);
            if (stream.Length > int.MaxValue)
            {
                throw new InvalidOperationException("Screenshot file is too large to retain.");
            }

            var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Screenshot file failed SHA-256 verification during registration.");
            }

            stream.Position = 0;
            var reader = new ScreenshotResourceReader(stream, checked((int)stream.Length), actualSha256);
            stream = null;
            return reader;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    internal byte[] ReadAll()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            var bytes = new byte[Length];
            _stream.Position = 0;
            _stream.ReadExactly(bytes);
            return bytes;
        }
    }

    internal byte[] ReadRange(int offset, int length)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            var bytes = new byte[Math.Min(length, Length - offset)];
            _stream.Position = offset;
            _stream.ReadExactly(bytes);
            return bytes;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stream.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ScreenshotResourceReader));
        }
    }
}
