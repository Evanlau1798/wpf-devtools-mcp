using System;
using System.IO;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Inspector.Sdk;

/// <summary>
/// Resolves the optional SDK-mode transport hardening settings from environment variables.
/// Unlike the standard injection path, SDK mode does not receive a generated handoff automatically,
/// so security is enabled here only when explicit matching configuration is provided.
/// </summary>
internal sealed class InspectorSdkTransportSecurityConfiguration
{
    private InspectorSdkTransportSecurityConfiguration(
        AuthenticationManager? authenticationManager,
        CertificateManager? certificateManager)
    {
        AuthenticationManager = authenticationManager;
        CertificateManager = certificateManager;
    }

    public AuthenticationManager? AuthenticationManager { get; }

    public CertificateManager? CertificateManager { get; }

    public bool IsAuthenticationEnabled => AuthenticationManager?.IsAuthenticationEnabled == true;

    public bool IsEncryptionEnabled => CertificateManager != null;

    public static InspectorSdkTransportSecurityConfiguration Create(
        string? authenticationSecretBase64,
        string? certificateDirectory)
    {
        ValidateExplicitTransportConfiguration(authenticationSecretBase64, certificateDirectory);

        var certificateManager = string.IsNullOrWhiteSpace(certificateDirectory)
            ? null
            : new CertificateManager(ResolveExplicitCertificateDirectory(certificateDirectory));

        if (certificateManager != null)
        {
            using var _ = certificateManager.GetOrCreateCertificate();
        }

        var authenticationManager = string.IsNullOrWhiteSpace(authenticationSecretBase64)
            ? null
            : new AuthenticationManager(() => authenticationSecretBase64);

        return new InspectorSdkTransportSecurityConfiguration(authenticationManager, certificateManager);
    }

    private static void ValidateExplicitTransportConfiguration(
        string? authenticationSecretBase64,
        string? certificateDirectory)
    {
        var hasAuthentication = !string.IsNullOrWhiteSpace(authenticationSecretBase64);
        var hasCertificateDirectory = !string.IsNullOrWhiteSpace(certificateDirectory);

        if (hasAuthentication == hasCertificateDirectory)
        {
            return;
        }

        throw new InvalidOperationException(
            "SDK transport hardening requires both WPFDEVTOOLS_AUTH_SECRET and WPFDEVTOOLS_CERT_DIR to be set together. " +
            "Set both values for hardened SDK mode, or leave both unset for plaintext SDK mode.");
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