using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ServerDistributionCompatibilityDocumentationTests
{
    [Fact]
    public void Program_ShouldStillUseAssemblyBasedMcpDiscovery()
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath("src/WpfDevTools.Mcp.Server/Program.cs"));

        content.Should().Contain(".WithToolsFromAssembly()");
        content.Should().Contain(".WithPromptsFromAssembly(typeof(WorkflowPrompts).Assembly)");
        content.Should().Contain(".WithResourcesFromAssembly(typeof(CapabilityResources).Assembly)");
    }

    [Theory]
    [InlineData("docfx/production/deployment.md")]
    [InlineData("docfx/zh-tw/production/deployment.md")]
    public void DeploymentDocs_ShouldDocumentServerNativeAotAndTrimmingBoundary(string relativePath)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));

        content.Should().Contain("Native AOT");
        content.Should().Contain("trimming");
        content.Should().Contain("WithToolsFromAssembly");
        content.Should().Contain("WithPromptsFromAssembly");
        content.Should().Contain("WithResourcesFromAssembly");
        content.Should().Contain("RequiresUnreferencedCode");
        content.Should().Contain("non-AOT");
    }
}
