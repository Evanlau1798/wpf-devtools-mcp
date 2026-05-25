#if NET48
using System;

namespace System.Security.Cryptography;

/// <summary>
/// Polyfill for CryptographicOperations in .NET Framework 4.8
/// </summary>
internal static class CryptographicOperations
{
    /// <summary>
    /// Compares two byte arrays in constant time to prevent timing attacks.
    ///
    /// SECURITY NOTE: The early return on length mismatch does leak whether the lengths differ.
    /// This is safe for our usage because all callers compare HMAC-SHA256 outputs (always 32 bytes).
    /// Do NOT use this method for comparing variable-length secrets where length itself is sensitive.
    /// This matches the behavior of .NET's built-in CryptographicOperations.FixedTimeEquals.
    /// </summary>
    /// <param name="left">First byte array (must be non-null)</param>
    /// <param name="right">Second byte array (must be non-null)</param>
    /// <returns>True if arrays are equal, false otherwise</returns>
    public static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left == null)
            throw new ArgumentNullException(nameof(left));

        if (right == null)
            throw new ArgumentNullException(nameof(right));

        if (left.Length != right.Length)
            return false;

        int result = 0;
        for (int i = 0; i < left.Length; i++)
        {
            result |= left[i] ^ right[i];
        }

        return result == 0;
    }

    /// <summary>
    /// Clears a sensitive byte buffer in-place.
    /// </summary>
    /// <param name="buffer">Buffer to clear.</param>
    public static void ZeroMemory(byte[] buffer)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        Array.Clear(buffer, 0, buffer.Length);
    }
}
#endif
