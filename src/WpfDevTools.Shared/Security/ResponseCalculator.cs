using System;
using System.Security.Cryptography;
using System.Threading;

namespace WpfDevTools.Shared.Security;

/// <summary>
/// Calculates and verifies HMAC-SHA256 responses for challenge-response authentication.
/// Implements IDisposable to securely zero the shared secret from memory on disposal.
/// </summary>
public sealed class ResponseCalculator : IDisposable
{
    private readonly byte[] _sharedSecret;
    private int _disposeState;

    /// <summary>
    /// Initializes a new instance of ResponseCalculator
    /// </summary>
    /// <param name="sharedSecret">Shared secret key (must be non-null and non-empty)</param>
    /// <exception cref="ArgumentNullException">Thrown when sharedSecret is null</exception>
    /// <exception cref="ArgumentException">Thrown when sharedSecret is empty</exception>
    public ResponseCalculator(byte[] sharedSecret)
    {
        if (sharedSecret == null)
            throw new ArgumentNullException(nameof(sharedSecret));

        if (sharedSecret.Length == 0)
            throw new ArgumentException("Shared secret cannot be empty", nameof(sharedSecret));

        _sharedSecret = (byte[])sharedSecret.Clone();
    }

    /// <summary>
    /// Computes HMAC-SHA256 response for the given challenge
    /// </summary>
    /// <param name="challenge">Challenge bytes</param>
    /// <returns>32-byte HMAC-SHA256 hash</returns>
    /// <exception cref="ArgumentNullException">Thrown when challenge is null</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed</exception>
    public byte[] ComputeResponse(byte[] challenge)
    {
        ThrowIfDisposed();

        if (challenge == null)
            throw new ArgumentNullException(nameof(challenge));

        using var hmac = new HMACSHA256(_sharedSecret);
        return hmac.ComputeHash(challenge);
    }

    /// <summary>
    /// Verifies that the response matches the expected HMAC-SHA256 hash of the challenge.
    /// Uses constant-time comparison to prevent timing attacks.
    /// </summary>
    /// <param name="challenge">Challenge bytes</param>
    /// <param name="response">Response bytes to verify</param>
    /// <returns>True if response is valid, false otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when challenge or response is null</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed</exception>
    public bool VerifyResponse(byte[] challenge, byte[] response)
    {
        ThrowIfDisposed();

        if (challenge == null)
            throw new ArgumentNullException(nameof(challenge));

        if (response == null)
            throw new ArgumentNullException(nameof(response));

        var expected = ComputeResponse(challenge);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, response);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    /// <summary>
    /// Securely zeros the shared secret from memory and releases resources
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
            return;

        CryptographicOperations.ZeroMemory(_sharedSecret);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
            throw new ObjectDisposedException(nameof(ResponseCalculator));
    }
}
