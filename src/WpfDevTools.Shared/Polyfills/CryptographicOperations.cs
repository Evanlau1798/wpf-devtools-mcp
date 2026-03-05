#if NET48
using System;

namespace System.Security.Cryptography;

/// <summary>
/// Polyfill for CryptographicOperations in .NET Framework 4.8
/// </summary>
internal static class CryptographicOperations
{
    /// <summary>
    /// Compares two byte arrays in constant time to prevent timing attacks
    /// </summary>
    /// <param name="left">First byte array</param>
    /// <param name="right">Second byte array</param>
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
}
#endif
