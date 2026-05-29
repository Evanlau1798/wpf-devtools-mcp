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
    /// TLS 1.3 is intentionally not enabled until scripts/tests/Test-TlsNegotiation.ps1
    /// verifies stable named-pipe SslStream negotiation for net8-net8, net8-net48,
    /// and net48-net8 runtime pairs.
    /// </remarks>
    public static SslProtocols InspectorTransport
    {
        get
        {
            return SslProtocols.Tls12;
        }
    }
}
