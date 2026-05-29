using System.Security.Authentication;
using FluentAssertions;
using WpfDevTools.Shared.Security;
using Xunit;

namespace WpfDevTools.Tests.Unit.Security;

public class SecureTransportProtocolsTests
{
    [Fact]
    public void InspectorTransport_ShouldRemainTls12ForNamedPipeCompatibility()
    {
        SecureTransportProtocols.InspectorTransport.Should().Be(SslProtocols.Tls12);
    }

    [Fact]
    public void SecureTransportProtocolsSource_ShouldNotEnableTls13UntilNamedPipeRuntimeIsVerified()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Shared/Security/SecureTransportProtocols.cs"));

        content.Should().Contain("return SslProtocols.Tls12;");
        content.Should().NotContain("SslProtocols.Tls13");
    }

    [Fact]
    public void SecureTransportProtocolsSource_ShouldDocumentTls13ReevaluationHarness()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Shared/Security/SecureTransportProtocols.cs"));

        content.Should().Contain("scripts/tests/Test-TlsNegotiation.ps1",
            "future TLS policy changes should point maintainers to the cross-runtime named-pipe harness");
        content.Should().Contain("net8-net8");
        content.Should().Contain("net8-net48");
        content.Should().Contain("net48-net8");
    }

    [Fact]
    public void ClientAndServerSource_ShouldUseSharedSecureTransportProtocolPolicy()
    {
        var client = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Mcp.Server/NamedPipeClient.Transport.cs"));
        var server = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Inspector/Host/InspectorHostSecurity.cs"));

        client.Should().Contain("SecureTransportProtocols.InspectorTransport");
        server.Should().Contain("SecureTransportProtocols.InspectorTransport");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
