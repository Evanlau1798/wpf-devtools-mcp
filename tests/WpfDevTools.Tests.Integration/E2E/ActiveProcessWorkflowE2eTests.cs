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
        var solutionDir = FindSolutionRoot();
        var candidates = new[]
        {
            Path.Combine(solutionDir, "src", "WpfDevTools.Mcp.Server", "bin", "Debug", "net8.0", "WpfDevTools.Mcp.Server.exe"),
            Path.Combine(solutionDir, "src", "WpfDevTools.Mcp.Server", "bin", "Release", "net8.0", "WpfDevTools.Mcp.Server.exe")
        };

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("MCP Server executable not found. Build src/WpfDevTools.Mcp.Server first.");
    }

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WpfDevTools.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Solution root not found");
    }
}
