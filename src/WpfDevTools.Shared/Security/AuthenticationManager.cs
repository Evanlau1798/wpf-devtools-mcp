using System;
using System.Security.Cryptography;

namespace WpfDevTools.Shared.Security;

/// <summary>
/// Manages shared secret for challenge-response authentication.
/// Supports loading from environment variable or auto-generating.
/// </summary>
public class AuthenticationManager
{
    private const int MinSecretLength = 32;
    private const string EnvVarName = "WPFDEVTOOLS_AUTH_SECRET";

    private readonly byte[]? _sharedSecret;
    private readonly bool _isEnabled;

    /// <summary>
    /// Creates an AuthenticationManager that loads secret from environment or auto-generates one.
    /// </summary>
    /// <param name="envSecretProvider">
    /// Optional function to provide the environment variable value.
    /// Defaults to reading WPFDEVTOOLS_AUTH_SECRET.
    /// </param>
    public AuthenticationManager(Func<string?>? envSecretProvider = null)
    {
        var provider = envSecretProvider ?? (() => Environment.GetEnvironmentVariable(EnvVarName));
        _sharedSecret = LoadOrGenerateSecret(provider);
        _isEnabled = true;
    }

    private AuthenticationManager(bool enabled)
    {
        _isEnabled = enabled;
        _sharedSecret = null;
    }

    /// <summary>
    /// Creates a disabled AuthenticationManager (for testing/development)
    /// </summary>
    public static AuthenticationManager CreateDisabled()
    {
        return new AuthenticationManager(enabled: false);
    }

    /// <summary>
    /// Whether authentication is enabled
    /// </summary>
    public bool IsAuthenticationEnabled => _isEnabled;

    /// <summary>
    /// Gets the shared secret bytes
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when authentication is disabled</exception>
    public byte[] GetSharedSecret()
    {
        if (!_isEnabled)
            throw new InvalidOperationException("Authentication is disabled. Cannot retrieve shared secret.");

        return _sharedSecret!;
    }

    private static byte[] LoadOrGenerateSecret(Func<string?> envSecretProvider)
    {
        var envSecret = envSecretProvider();

        if (!string.IsNullOrEmpty(envSecret))
        {
            var decoded = Convert.FromBase64String(envSecret);
            if (decoded.Length < MinSecretLength)
                throw new ArgumentException(
                    $"Shared secret must be at least {MinSecretLength} bytes ({MinSecretLength * 8} bits). " +
                    $"Provided secret is {decoded.Length} bytes.");

            return decoded;
        }

        return GenerateSecureSecret();
    }

    private static byte[] GenerateSecureSecret()
    {
        var secret = new byte[MinSecretLength];
        RandomNumberGenerator.Fill(secret);
        return secret;
    }
}
