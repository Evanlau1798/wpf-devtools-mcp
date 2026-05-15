using System.Security.Authentication;

namespace WpfDevTools.Shared.Security;

/// <summary>
/// TLS protocol policy shared by the MCP server and injected inspector transport.
/// </summary>
public static class SecureTransportProtocols
{
    /// <summary>
    /// Protocols allowed for the local secure named-pipe SslStream transport.
    /// </summary>
    /// <remarks>
    /// TLS 1.3 is intentionally not enabled until named-pipe SslStream behavior is verified across supported runtimes.
    /// </remarks>
    public static SslProtocols InspectorTransport
    {
        get
        {
            return SslProtocols.Tls12;
        }
    }
}