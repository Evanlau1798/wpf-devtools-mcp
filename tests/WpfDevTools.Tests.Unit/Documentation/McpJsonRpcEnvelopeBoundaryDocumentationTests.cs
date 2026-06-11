using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class McpJsonRpcEnvelopeBoundaryDocumentationTests
{
    [Theory]
    [InlineData("SECURITY.md")]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    public void SecurityDocumentation_ShouldDescribeSdkOwnedMcpJsonRpcEnvelopeBoundary(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("MCP JSON-RPC envelope",
            $"{relativePath} should name the raw MCP transport envelope boundary");
        content.Should().Contain("MCP C# SDK",
            $"{relativePath} should identify the component that parses pre-dispatch envelope fields");
        content.Should().Contain("SDK-owned",
            $"{relativePath} should avoid implying that this project validates raw pre-dispatch envelopes itself");
        content.Should().Contain("initialize");
        content.Should().Contain("resources/read");
        content.Should().Contain("tools/list");
        content.Should().Contain("tool-call names and arguments",
            $"{relativePath} should state the MCP boundary this server validates after SDK parsing");
        content.Should().Contain("Inspector IPC request ids, methods, and correlation ids",
            $"{relativePath} should state the downstream IPC boundary enforced by this project");
    }

    [Theory]
    [InlineData("SECURITY.md")]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    public void SecurityDocumentation_ShouldDescribeInjectedAndSdkHostedInspectorHosts(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("injected or SDK-hosted Inspector host",
            $"{relativePath} should not imply that IPC validation only protects injected hosts");
        content.Should().NotContain("injected host",
            $"{relativePath} should use the broader Inspector host boundary wording");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
