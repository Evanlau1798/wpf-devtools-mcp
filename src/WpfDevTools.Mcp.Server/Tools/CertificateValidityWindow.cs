namespace WpfDevTools.Mcp.Server.Tools;

internal readonly record struct CertificateValidityWindow(
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter)
{
    public bool Contains(DateTimeOffset timestamp)
        => timestamp >= NotBefore && timestamp <= NotAfter;
}
