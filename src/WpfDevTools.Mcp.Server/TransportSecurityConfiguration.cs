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
        Func<CertificateManager>? defaultCertificateManagerFactory = null,
        Func<string>? defaultAppDataPathProvider = null)
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
                var effectiveSecretStore = secretStore ?? CreateDefaultSecretStore(defaultAppDataPathProvider);
                resolvedAuthenticationSecretBase64 = effectiveSecretStore.GetOrCreateSecretBase64();
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
                certificateManager = (defaultCertificateManagerFactory
                    ?? CreateDefaultCertificateManagerFactory(defaultAppDataPathProvider))();
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

            return CertificateStorageSecurity.ResolveAndValidateLocalPath(
                certificateDirectory,
                nameof(certificateDirectory));
        }
        catch (ArgumentException ex)
        {
            if (string.Equals(ex.ParamName, nameof(certificateDirectory), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"WPFDEVTOOLS_CERT_DIR must be a local path. Network paths are not allowed. Received '{certificateDirectory}'.",
                    ex);
            }

            throw new InvalidOperationException(
                $"WPFDEVTOOLS_CERT_DIR must resolve to a valid absolute path. Received '{certificateDirectory}'.",
                ex);
        }
        catch (Exception ex) when (ex is NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException(
                $"WPFDEVTOOLS_CERT_DIR must resolve to a valid absolute path. Received '{certificateDirectory}'.",
                ex);
        }
    }

    private static PersistedAuthenticationSecretStore CreateDefaultSecretStore(
        Func<string>? defaultAppDataPathProvider)
        => defaultAppDataPathProvider == null
            ? new PersistedAuthenticationSecretStore()
            : PersistedAuthenticationSecretStore.CreateForDefaultProfile(defaultAppDataPathProvider);

    private static Func<CertificateManager> CreateDefaultCertificateManagerFactory(
        Func<string>? defaultAppDataPathProvider)
        => defaultAppDataPathProvider == null
            ? static () => new CertificateManager()
            : () => CertificateManager.CreateForDefaultProfile(defaultAppDataPathProvider);

    private static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            return path.Length >= 7
                && char.IsLetter(path[4])
                && path[5] == ':'
                && (path[6] == Path.DirectorySeparatorChar || path[6] == Path.AltDirectorySeparatorChar);
        }

        if (IsNetworkPath(certificateDirectory: path))
        {
            return true;
        }

        return path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == Path.DirectorySeparatorChar || path[2] == Path.AltDirectorySeparatorChar);
    }

    private static bool IsNetworkPath(string certificateDirectory)
    {
        if (string.IsNullOrWhiteSpace(certificateDirectory))
        {
            return false;
        }

        if (certificateDirectory.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
            && !certificateDirectory.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return certificateDirectory.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
            || certificateDirectory.StartsWith(@"\\", StringComparison.Ordinal)
            || certificateDirectory.StartsWith("//", StringComparison.Ordinal);
    }
}
