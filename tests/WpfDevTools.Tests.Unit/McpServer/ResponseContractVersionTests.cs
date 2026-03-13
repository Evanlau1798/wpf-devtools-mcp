using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.Schema;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ResponseContractVersionTests
{
    [Fact]
    public void Current_ShouldExposeStableResponseContractVersion()
    {
        ResponseContractVersion.Current.Should().NotBeNullOrWhiteSpace();
        ResponseContractVersion.DeprecatedAliases.Should().NotBeEmpty();
    }

    [Fact]
    public void CapabilitiesResource_ShouldDescribeResponseContractVersionAndCompatibilityAliases()
    {
        var content = CapabilityResources.GetCapabilities();

        content.Should().Contain(ResponseContractVersion.Current);
        content.Should().Contain("Compatibility aliases");
        content.Should().Contain("currentValue -> effectiveValue");
        content.Should().Contain("typeName -> viewModelType");
        content.Should().Contain("avgRenderTime -> averageFrameTime");
        content.Should().Contain("detail=compact");
        content.Should().Contain("nextSteps");
        content.Should().Contain("preconditions");
        content.Should().Contain("prefetchTools");
        content.Should().Contain("navigation");
        content.Should().Contain("contextRefs");
        content.Should().Contain("additive");
        content.Should().Contain("descriptive JSON");
    }

    [Fact]
    public void ServerInstructions_ShouldDescribeCompatibilityAliasesAndCompactMode()
    {
        ServerInstructions.Value.Should().Contain("RESPONSE CONTRACT VERSION");
        ServerInstructions.Value.Should().Contain(ResponseContractVersion.Current);
        ServerInstructions.Value.Should().Contain("Compatibility aliases");
        ServerInstructions.Value.Should().Contain("detail=compact");
        ServerInstructions.Value.Should().Contain("nextSteps");
        ServerInstructions.Value.Should().Contain("preconditions");
        ServerInstructions.Value.Should().Contain("workflowId");
        ServerInstructions.Value.Should().Contain("prefetchTools");
        ServerInstructions.Value.Should().Contain("navigation");
        ServerInstructions.Value.Should().Contain("contextRef");
        ServerInstructions.Value.Should().Contain("additive");
        ServerInstructions.Value.Should().Contain("compatibility field");
        ServerInstructions.Value.Should().Contain("descriptive JSON");
    }
}
