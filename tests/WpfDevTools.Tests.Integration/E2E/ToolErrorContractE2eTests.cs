using System.IO;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

public class ToolErrorContractE2eTests : IDisposable
{
    private readonly McpStdioClient _client = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetVisualTree_WithoutConnect_ShouldReturnStructuredNotConnectedError()
    {
        var serverExe = FindServerExe();
        await _client.StartAsync(serverExe);

        var result = await _client.CallToolAsync(
            "get_visual_tree",
            new { processId = 12345, depth = 1 });

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("NotConnected");
        result.GetProperty("hint").GetString().Should().Contain("connect");
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static string FindServerExe()
    {
        return IntegrationExecutableLocator.FindExecutable(
                AppContext.BaseDirectory,
                "src",
                "WpfDevTools.Mcp.Server",
                "net8.0",
                "WpfDevTools.Mcp.Server.exe")
            ?? throw new InvalidOperationException(
                "MCP Server executable not found for the current test configuration. Build src/WpfDevTools.Mcp.Server first.");
    }
}

[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class InspectorToolErrorContractE2eTests
{
    private readonly McpE2eFixture _fixture;

    public InspectorToolErrorContractE2eTests(McpE2eFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ClickElement_WithMissingElement_ShouldPreserveInspectorStructuredError()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "click_element",
            new { processId = _fixture.TestAppProcessId, elementId = "NonExistent_999" });

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("ElementNotFound");
        result.GetProperty("hint").GetString().Should().Contain("elementId");
        result.GetProperty("error").GetString().Should().Contain("not found");
    }
}
