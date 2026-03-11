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
