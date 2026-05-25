using System.IO;

namespace WpfDevTools.Inspector.Sdk;

public static partial class InspectorSdk
{
    private static InspectorSdkInitializationStatus CreateNotStartedStatus() => new(
        "NotStarted",
        false,
        null,
        null,
        null,
        null,
        null,
        DateTimeOffset.UtcNow);

    private static InspectorSdkInitializationStatus CreateInitializingStatus(int processId) => new(
        "Initializing",
        false,
        processId,
        null,
        null,
        null,
        null,
        DateTimeOffset.UtcNow);

    private static InspectorSdkInitializationStatus CreateInitializedStatus(int processId) => new(
        "Initialized",
        true,
        processId,
        null,
        null,
        null,
        null,
        DateTimeOffset.UtcNow);

    private static InspectorSdkInitializationStatus CreateFailedStatus(int processId, Exception exception) => new(
        "Failed",
        false,
        processId,
        GetInitializationErrorCode(exception),
        exception.GetType().Name,
        exception.Message,
        GetInitializationHint(exception),
        DateTimeOffset.UtcNow);

    private static string GetInitializationErrorCode(Exception exception)
    {
        if (exception is FormatException)
        {
            return "SdkAuthenticationSecretInvalid";
        }

        if (IsCertificateDirectoryException(exception))
        {
            return "SdkCertificateDirectoryInvalid";
        }

        if (exception is TimeoutException)
        {
            return "SdkDispatcherTimeout";
        }

        if (exception is InvalidOperationException &&
            (ContainsOrdinal(exception.Message, "WPFDEVTOOLS_AUTH_SECRET") ||
             ContainsOrdinal(exception.Message, "WPFDEVTOOLS_CERT_DIR") ||
             ContainsOrdinal(exception.Message, "set together")))
        {
            return "SdkTransportConfigurationInvalid";
        }

        if (exception is InvalidOperationException && ContainsOrdinal(exception.Message, "dispatcher"))
        {
            return "SdkDispatcherUnavailable";
        }

        return "SdkInitializationFailed";
    }

    private static string GetInitializationHint(Exception exception)
    {
        if (string.Equals(GetInitializationErrorCode(exception), "SdkTransportConfigurationInvalid", StringComparison.Ordinal))
        {
            return "set both WPFDEVTOOLS_AUTH_SECRET and WPFDEVTOOLS_CERT_DIR to matching values before calling InspectorSdk.Initialize().";
        }

        if (exception is FormatException)
        {
            return "Provide WPFDEVTOOLS_AUTH_SECRET as a base64-encoded 32-byte shared secret.";
        }

        if (IsCertificateDirectoryException(exception))
        {
            return "Provide WPFDEVTOOLS_CERT_DIR or InspectorSdkOptions.CertificateDirectory as a writable local absolute directory shared with the MCP server. Network paths are not allowed.";
        }

        if (exception is TimeoutException)
        {
            return "Ensure the target-side WPF dispatcher can process InspectorSdk.Initialize() within the UI-thread timeout.";
        }

        return "Inspect ErrorType and ErrorMessage, then retry initialization after correcting the target-side SDK configuration.";
    }

    private static bool ContainsOrdinal(string value, string expected)
        => value.Contains(expected, StringComparison.Ordinal);

    private static bool IsCertificateDirectoryException(Exception exception)
        => exception is IOException
            || (exception is ArgumentException && ContainsOrdinal(exception.Message, "Certificate directory"))
            || (exception is InvalidOperationException &&
                (ContainsOrdinal(exception.Message, "WPFDEVTOOLS_CERT_DIR must") ||
                 ContainsOrdinal(exception.Message, "InspectorSdkOptions.CertificateDirectory must")));
}
