using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class McpSdkPlanDocumentationTests
{
    [Fact]
    public void Readme_ShouldDocumentCreateEmptyApplicationBuilder()
    {
        var readmeContent = File.ReadAllText(GetRepoFilePath("README.md"));

        readmeContent.Should().Contain("Host.CreateEmptyApplicationBuilder");
        readmeContent.Should().NotContain("Host.CreateApplicationBuilder(args)");
    }

    [Fact]
    public void Readme_ShouldExplainWhyEmptyBuilderIsUsedForStdio()
    {
        var readmeContent = File.ReadAllText(GetRepoFilePath("README.md"));

        readmeContent.Should().Contain("stdout");
        readmeContent.Should().Contain("STDIO");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
