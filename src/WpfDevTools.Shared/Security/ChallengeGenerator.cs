using System;
using System.Security.Cryptography;

namespace WpfDevTools.Shared.Security;

/// <summary>
/// Generates cryptographically secure random challenges for authentication
/// </summary>
public class ChallengeGenerator
{
    /// <summary>
    /// Generates a cryptographically secure random challenge
    /// </summary>
    /// <returns>32-byte random challenge</returns>
    public byte[] GenerateChallenge()
    {
        var challenge = new byte[32]; // 256 bits
#if NET48
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(challenge);
        }
#else
        RandomNumberGenerator.Fill(challenge);
#endif
        return challenge;
    }
}
