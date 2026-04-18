using System.IO;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

public class ActiveProcessWorkflowE2eTests : IDisposable
{
    private readonly McpStdioClient _client = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetActiveProcess_BeforeSelection_ShouldReturnEmptyState()
    {
        await _client.StartAsync(FindServerExe());

        var result = await _client.CallToolAsync("get_active_process");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("hasActiveProcess").GetBoolean().Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SelectActiveProcess_WithoutConnectedSession_ShouldReturnStructuredError()
    {
        await _client.StartAsync(FindServerExe());

        var result = await _client.CallToolAsync(
            "select_active_process",
            new { processId = 12345 });

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("NotConnected");
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
