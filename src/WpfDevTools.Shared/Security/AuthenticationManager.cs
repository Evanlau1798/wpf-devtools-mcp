using System;
using System.Security.Cryptography;

namespace WpfDevTools.Shared.Security;

/// <summary>
/// Manages shared secret for challenge-response authentication.
/// Supports loading from environment variable or auto-generating.
/// Implements IDisposable to securely zero the shared secret from memory.
/// </summary>
public sealed class AuthenticationManager : IDisposable
{
    private const int MinSecretLength = 32;
    private const string EnvVarName = "WPFDEVTOOLS_AUTH_SECRET";

    private readonly byte[]? _sharedSecret;
    private readonly bool _isEnabled;
    private volatile bool _isDisposed;

    /// <summary>
    /// Creates an AuthenticationManager that loads secret from environment or auto-generates one.
    /// <para>
    /// WARNING: When no environment variable is set, a random secret is auto-generated.
    /// Both the MCP Server and the Inspector DLL must share the same secret for authentication
    /// to succeed. In production, always set WPFDEVTOOLS_AUTH_SECRET to a shared base64 secret.
    /// Use <see cref="CreateDisabled"/> when authentication is not needed (e.g., testing).
    /// </para>
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
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(AuthenticationManager));

        if (!_isEnabled)
            throw new InvalidOperationException("Authentication is disabled. Cannot retrieve shared secret.");

        return (byte[])_sharedSecret!.Clone();
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
#if NET48
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(secret);
        }
#else
        RandomNumberGenerator.Fill(secret);
#endif
        return secret;
    }

    /// <summary>
    /// Securely zeros the shared secret from memory
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        if (_sharedSecret != null)
        {
#if NET48
            // Array.Clear is an internal CLR call and not optimized away by the .NET Framework JIT
            Array.Clear(_sharedSecret, 0, _sharedSecret.Length);
#else
            // CryptographicOperations.ZeroMemory is guaranteed not to be optimized away by the JIT
            CryptographicOperations.ZeroMemory(_sharedSecret);
#endif
        }
    }
}
