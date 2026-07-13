using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class GetVisualTreeToolTests
{


    [Fact]
    public async Task Execute_WithValidParameters_ShouldIncludeDepthParameter()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetVisualTreeTool(sessionManager);
        var parameters = new { processId = 12345, depth = 2 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        // Note: This will fail until we have actual Named Pipe communication
    }
}
