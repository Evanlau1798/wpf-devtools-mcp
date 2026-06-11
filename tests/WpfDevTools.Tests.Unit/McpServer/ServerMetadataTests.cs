using System.Reflection;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class ServerMetadataTests
{
    [Fact]
    public void GetDisplayVersion_ShouldReturnAssemblyInformationalVersionWithoutBuildMetadata()
    {
        var assembly = typeof(ServerInstructions).Assembly;
        var expected = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        expected.Should().NotBeNullOrWhiteSpace();
        var normalizedExpected = expected!.Split('+')[0];

        ServerMetadata.GetDisplayVersion().Should().Be(normalizedExpected);
    }
}
