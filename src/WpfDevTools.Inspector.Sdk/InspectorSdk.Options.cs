namespace WpfDevTools.Inspector.Sdk;

public static partial class InspectorSdk
{
    private static string? ResolveAuthenticationSecret(InspectorSdkOptions options)
    {
        ValidateExplicitTransportOptions(options);
        return HasExplicitTransportOptions(options)
            ? options.AuthenticationSecretBase64
            : Environment.GetEnvironmentVariable("WPFDEVTOOLS_AUTH_SECRET");
    }

    private static string? ResolveCertificateDirectory(InspectorSdkOptions options)
    {
        ValidateExplicitTransportOptions(options);
        return HasExplicitTransportOptions(options)
            ? options.CertificateDirectory
            : Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_DIR");
    }

    private static string ResolveCertificateDirectorySourceName(InspectorSdkOptions options)
    {
        ValidateExplicitTransportOptions(options);
        return HasExplicitTransportOptions(options)
            ? "InspectorSdkOptions.CertificateDirectory"
            : "WPFDEVTOOLS_CERT_DIR";
    }

    private static void ValidateExplicitTransportOptions(InspectorSdkOptions options)
    {
        var hasAuthenticationSecret = !string.IsNullOrWhiteSpace(options.AuthenticationSecretBase64);
        var hasCertificateDirectory = !string.IsNullOrWhiteSpace(options.CertificateDirectory);
        if (!hasAuthenticationSecret && !hasCertificateDirectory)
        {
            return;
        }

        if (hasAuthenticationSecret && hasCertificateDirectory)
        {
            return;
        }

        throw new InvalidOperationException(
            "InspectorSdkOptions.AuthenticationSecretBase64 and InspectorSdkOptions.CertificateDirectory must be set together. " +
            "Partial explicit SDK transport configuration is not supported and will not be combined with environment variables.");
    }

    private static bool HasExplicitTransportOptions(InspectorSdkOptions options)
        => !string.IsNullOrWhiteSpace(options.AuthenticationSecretBase64) ||
           !string.IsNullOrWhiteSpace(options.CertificateDirectory);
}
