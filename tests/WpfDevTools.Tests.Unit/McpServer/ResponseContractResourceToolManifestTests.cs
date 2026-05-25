using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ResponseContractResourceToolManifestTests
{
    [Fact]
    public void ResponseContractResource_ShouldLinkCanonicalToolManifest()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var root = document.RootElement;

        var manifest = root.GetProperty("toolManifest");
        manifest.GetProperty("resourceUri").GetString().Should().Be("wpf://contracts/tools");
        manifest.GetProperty("generatedFrom").GetString().Should().Be("McpServerToolAttribute");
        manifest.GetProperty("usage").GetString().Should().Contain("runtime tools/list parity");
    }
}
