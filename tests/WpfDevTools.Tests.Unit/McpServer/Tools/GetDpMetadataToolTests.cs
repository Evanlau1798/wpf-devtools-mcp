using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class GetDpMetadataToolTests
{


    [Fact]
    public async Task Execute_WithMissingPropertyName_ShouldReturnError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetDpMetadataTool(sessionManager);
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("propertyName");
    }

}
