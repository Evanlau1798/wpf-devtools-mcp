using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public class ReadmeDocumentationTests
{
    [Fact]
    public void Readme_ShouldStayUnderFiveHundredLines()
    {
        var lines = File.ReadAllLines(GetRepoFilePath("README.md"));

        lines.Length.Should().BeLessThanOrEqualTo(500);
    }

    [Fact]
    public void Readme_ShouldDescribeCurrentStdioOnlyState()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("STDIO");
        content.Should().Contain("WithStdioServerTransport");
        content.Should().NotContain("HTTP+SSE currently available");
    }

    [Fact]
    public void Readme_ShouldAvoidRawJsonRpcTutorials()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().NotContain("tools/call");
        content.Should().NotContain("\"jsonrpc\"");
    }

    [Fact]
    public void Readme_ShouldReferenceOnlyCommittedRepositoryDocs()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("SECURITY.md");
        content.Should().Contain("EXAMPLES.md");
        content.Should().NotContain("docs/current-state.md");
        content.Should().NotContain("docs/mcp-sdk-plan/README.md");
        content.Should().NotContain("docs/development-plan/README.md");
        content.Should().NotContain("docs/architecture/");
    }

    [Fact]
    public void Readme_ShouldDocumentStructuredInspectorErrors()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("errorCode");
        content.Should().Contain("errorData");
    }

    [Fact]
    public void Readme_ShouldListBootstrapperAndInspectorSdkProjectsInRepositoryLayout()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("WpfDevTools.Bootstrapper/");
        content.Should().Contain("WpfDevTools.Inspector.Sdk/");
    }

    [Fact]
    public void Program_ShouldUseEmptyApplicationBuilder_ForStdioServer()
    {
        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/Program.cs"));

        content.Should().Contain("Host.CreateEmptyApplicationBuilder(");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
    [Fact]
    public void Readme_ShouldDocumentPublishedArtifactSetupAndServerBitness()
    {
        var content = File.ReadAllText(GetRepoFilePath("README.md"));

        content.Should().Contain("published release",
            "the public onboarding path should mention published artifacts instead of only source-tree startup");
        content.Should().Contain("server process architecture must match the target process",
            "README quick start must state that the MCP server/injector bitness must match the target app");
    }
}
