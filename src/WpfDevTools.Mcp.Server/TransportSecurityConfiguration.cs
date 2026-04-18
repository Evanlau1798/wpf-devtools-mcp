using System.Security.Cryptography;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Creates the transport security managers used by the MCP server.
/// Injection-based inspector sessions are hardened by default; environment variables
/// override the generated authentication secret or certificate directory when needed.
/// </summary>
internal sealed class TransportSecurityConfiguration
{
    private TransportSecurityConfiguration(
        AuthenticationManager authenticationManager,
        CertificateManager certificateManager,
        bool usesExplicitAuthenticationSecret,
        bool usesExplicitCertificateDirectory)
    {
        AuthenticationManager = authenticationManager;
        CertificateManager = certificateManager;
        UsesExplicitAuthenticationSecret = usesExplicitAuthenticationSecret;
        UsesExplicitCertificateDirectory = usesExplicitCertificateDirectory;
    }

    public AuthenticationManager AuthenticationManager { get; }

    public CertificateManager CertificateManager { get; }

    public bool UsesExplicitAuthenticationSecret { get; }

    public bool UsesExplicitCertificateDirectory { get; }

    public static TransportSecurityConfiguration Create(
        string? authenticationSecretBase64,
        string? certificateDirectory,
        PersistedAuthenticationSecretStore? secretStore = null,
        Func<CertificateManager>? defaultCertificateManagerFactory = null)
    {
        var usesExplicitAuthenticationSecret = !string.IsNullOrWhiteSpace(authenticationSecretBase64);
        var usesExplicitCertificateDirectory = !string.IsNullOrWhiteSpace(certificateDirectory);
        string? resolvedAuthenticationSecretBase64;
        string? resolvedCertificateDirectory = null;

        if (usesExplicitAuthenticationSecret)
        {
            resolvedAuthenticationSecretBase64 = authenticationSecretBase64;
        }
        else
        {
            try
            {
                resolvedAuthenticationSecretBase64 = (secretStore ?? new PersistedAuthenticationSecretStore()).GetOrCreateSecretBase64();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or CryptographicException or TimeoutException)
            {
                throw new InvalidOperationException(
                    "Failed to create or load the default persisted authentication secret. " +
                    "Ensure the current user profile storage is available, or set WPFDEVTOOLS_AUTH_SECRET explicitly.",
                    ex);
            }
        }

        if (usesExplicitCertificateDirectory)
        {
            resolvedCertificateDirectory = ResolveExplicitCertificateDirectory(certificateDirectory!);
        }

        CertificateManager certificateManager;

        if (usesExplicitCertificateDirectory)
        {
            certificateManager = new CertificateManager(resolvedCertificateDirectory!);
        }
        else
        {
            try
            {
                certificateManager = (defaultCertificateManagerFactory ?? (() => new CertificateManager()))();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    "Failed to create or resolve the default TLS certificate directory. " +
                    "Ensure the current user profile storage is available, or set WPFDEVTOOLS_CERT_DIR explicitly to an absolute writable directory.",
                    ex);
            }
        }

        var authenticationManager = new AuthenticationManager(
            () => resolvedAuthenticationSecretBase64);

        return new TransportSecurityConfiguration(
            authenticationManager,
            certificateManager,
            usesExplicitAuthenticationSecret,
            usesExplicitCertificateDirectory);
    }

    public string GetAuthenticationLogMessage()
    {
        return UsesExplicitAuthenticationSecret
            ? "Authentication enabled via WPFDEVTOOLS_AUTH_SECRET"
            : "Authentication enabled with a persisted default shared secret for injection-based inspector sessions";
    }

    public string GetEncryptionLogMessage()
    {
        return UsesExplicitCertificateDirectory
            ? $"TLS encryption enabled via WPFDEVTOOLS_CERT_DIR: {CertificateManager.CertificateDirectory}"
            : $"TLS encryption enabled with the default certificate directory: {CertificateManager.CertificateDirectory}";
    }

    private static string ResolveExplicitCertificateDirectory(string certificateDirectory)
    {
        try
        {
            if (!IsAbsolutePath(certificateDirectory))
            {
                throw new InvalidOperationException(
                    $"WPFDEVTOOLS_CERT_DIR must be an absolute path. Received '{certificateDirectory}'.");
            }

            return Path.GetFullPath(certificateDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException(
                $"WPFDEVTOOLS_CERT_DIR must resolve to a valid absolute path. Received '{certificateDirectory}'.",
                ex);
        }
    }

    private static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return true;
        }

        return path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == Path.DirectorySeparatorChar || path[2] == Path.AltDirectorySeparatorChar);
    }
}