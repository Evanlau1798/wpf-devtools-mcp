using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class McpSdkPlanDocumentationTests
{
    [Fact]
    public void Program_ShouldUseEmptyApplicationBuilderForStdio()
    {
        var programContent = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/Program.cs"));

        programContent.Should().Contain("Host.CreateEmptyApplicationBuilder");
        programContent.Should().NotContain("Host.CreateApplicationBuilder(args)");
    }

    [Fact]
    public void Program_ShouldExplainWhyEmptyBuilderIsUsedForStdio()
    {
        var programContent = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/Program.cs"));

        programContent.Should().Contain("stdout");
        programContent.Should().Contain("STDIO");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
