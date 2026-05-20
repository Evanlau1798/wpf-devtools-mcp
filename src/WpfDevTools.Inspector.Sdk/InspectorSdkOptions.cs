namespace WpfDevTools.Inspector.Sdk;

/// <summary>
/// Explicit initialization options for the target-side WPF DevTools Inspector SDK.
/// </summary>
public sealed record InspectorSdkOptions
{
    /// <summary>
    /// Process ID reported by the target-side Inspector host. Defaults to the current process.
    /// </summary>
    public int? ProcessId { get; init; }

    /// <summary>
    /// Base64-encoded 32-byte shared secret used for authenticated SDK-host transport.
    /// Must be set together with <see cref="CertificateDirectory" /> when explicit transport options are used.
    /// </summary>
    public string? AuthenticationSecretBase64 { get; init; }

    /// <summary>
    /// A local absolute directory used for SDK-host transport certificates. Network paths are not allowed.
    /// Must be set together with <see cref="AuthenticationSecretBase64" /> when explicit transport options are used.
    /// </summary>
    public string? CertificateDirectory { get; init; }
}
