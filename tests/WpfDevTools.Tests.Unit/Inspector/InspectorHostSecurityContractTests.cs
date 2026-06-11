using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector;

public sealed class InspectorHostSecurityContractTests
{
    [Fact]
    public void InspectorHostSecurity_ShouldUseConfigurableHandshakeTimeouts()
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Inspector/Host/InspectorHost.Security.cs"));

        content.Should().Contain("InspectorConfig.AuthenticationTimeout",
            "authentication handshake timeouts should come from configuration instead of a hardcoded 5-second budget");
        content.Should().Contain("InspectorConfig.TlsHandshakeTimeout",
            "TLS handshake timeouts should come from configuration instead of a hardcoded 10-second budget");
    }
}
