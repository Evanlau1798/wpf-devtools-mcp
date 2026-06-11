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

    [Theory]
    [InlineData("docfx/production/compatibility-matrix.md")]
    [InlineData("docfx/zh-tw/production/compatibility-matrix.md")]
    public void CompatibilityMatrix_ShouldMatchShippedRawInjectionRuntimePaths(string relativePath)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));
        var inspectorProject = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Inspector/WpfDevTools.Inspector.csproj"));
        var runtimeSelector = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Injector/RuntimeSelector.cs"));

        inspectorProject.Should().Contain("<TargetFrameworks>net8.0-windows;net48</TargetFrameworks>");
        runtimeSelector.Should().Contain("runtime == TargetRuntime.NetFramework ? \"net48\" : \"net8.0-windows\"");
        content.Should().Contain(".NET 8+ WPF");
        content.Should().Contain(".NET 6/7 WPF");
        content.Should().NotContain(".NET 6/7/8+ WPF");
    }
}
