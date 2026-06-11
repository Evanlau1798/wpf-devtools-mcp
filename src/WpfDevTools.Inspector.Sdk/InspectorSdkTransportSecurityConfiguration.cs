using System;
using System.IO;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Inspector.Sdk;

/// <summary>
/// Resolves the required SDK-mode transport hardening settings from environment variables.
/// Unlike the standard injection path, SDK mode does not receive a generated handoff automatically,
/// so initialization fails closed unless explicit matching configuration is provided.
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
        string? certificateDirectory,
        string certificateDirectorySourceName = "WPFDEVTOOLS_CERT_DIR")
    {
        ValidateExplicitTransportConfiguration(authenticationSecretBase64, certificateDirectory);

        var certificateManager = string.IsNullOrWhiteSpace(certificateDirectory)
            ? null
            : new CertificateManager(ResolveExplicitCertificateDirectory(
                certificateDirectory,
                certificateDirectorySourceName));

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

        if (!hasAuthentication && !hasCertificateDirectory)
        {
            throw new InvalidOperationException(
                "SDK transport hardening requires both WPFDEVTOOLS_AUTH_SECRET and WPFDEVTOOLS_CERT_DIR to be set before calling InspectorSdk.Initialize(). " +
                "SDK plaintext mode is no longer supported by default.");
        }

        if (hasAuthentication == hasCertificateDirectory)
        {
            return;
        }

        throw new InvalidOperationException(
            "SDK transport hardening requires both WPFDEVTOOLS_AUTH_SECRET and WPFDEVTOOLS_CERT_DIR to be set together. " +
            "Partial SDK transport configuration is not supported.");
    }

    private static string ResolveExplicitCertificateDirectory(
        string certificateDirectory,
        string certificateDirectorySourceName)
    {
        try
        {
            if (!IsAbsolutePath(certificateDirectory))
            {
                throw new InvalidOperationException(
                    $"{certificateDirectorySourceName} must be an absolute path. Received '{certificateDirectory}'.");
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
                    $"{certificateDirectorySourceName} must be a local path. Network paths are not allowed, and reparse points are not allowed. Received '{certificateDirectory}'.",
                    ex);
            }

            throw new InvalidOperationException(
                $"{certificateDirectorySourceName} must resolve to a valid absolute path. Received '{certificateDirectory}'.",
                ex);
        }
        catch (Exception ex) when (ex is NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException(
                $"{certificateDirectorySourceName} must resolve to a valid absolute path. Received '{certificateDirectory}'.",
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
