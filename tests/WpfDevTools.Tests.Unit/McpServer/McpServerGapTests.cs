using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Gap tests for MCP Server layer to improve code coverage.
/// Covers: SessionManager, PipeConnectedToolBase, PingTool, Tree tools, GenericPipeTool.
/// </summary>
public class McpServerGapTests
{
    #region SessionManager - UpdateLastActivity for non-existent session

    [Fact]
    public void UpdateLastActivity_WithNonExistentSession_ShouldNotThrow()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        var act = () => manager.UpdateLastActivity(99999);

        // Assert - should silently do nothing
        act.Should().NotThrow();
    }

    [Fact]
    public void UpdateLastActivity_WithNonExistentSession_ShouldNotCreateSession()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        manager.UpdateLastActivity(99999);

        // Assert - session should not be created
        manager.HasSession(99999).Should().BeFalse();
        manager.GetActiveSessionCount().Should().Be(0);
    }

    #endregion

    #region SessionManager - GetLastActivityTime for non-existent session

    [Fact]
    public void GetLastActivityTime_WithNonExistentSession_ShouldReturnMinValue()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        var result = manager.GetLastActivityTime(99999);

        // Assert
        result.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void GetLastActivityTime_AfterRemovingSession_ShouldReturnMinValue()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(12345);
        var activityTime = manager.GetLastActivityTime(12345);
        activityTime.Should().NotBe(DateTime.MinValue); // sanity check: was valid

        manager.RemoveSession(12345);

        // Act
        var result = manager.GetLastActivityTime(12345);

        // Assert
        result.Should().Be(DateTime.MinValue);
    }

    #endregion

    #region SessionManager - GetPipeClient for non-existent processId

    [Fact]
    public void GetPipeClient_WithNonExistentProcessId_ShouldReturnNull()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        var result = manager.GetPipeClient(99999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetPipeClient_AfterRemovingSession_ShouldReturnNull()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(12345);
        manager.GetPipeClient(12345).Should().NotBeNull(); // sanity check

        manager.RemoveSession(12345);

        // Act
        var result = manager.GetPipeClient(12345);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region SessionManager - Dispose with multiple clients

    [Fact]
    public void Dispose_WithMultipleClients_ShouldClearAllSessions()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);
        manager.AddSession(300);
        manager.GetActiveSessionCount().Should().Be(3);

        // Act
        manager.Dispose();

        // Assert - all sessions should be cleared
        manager.GetActiveSessionCount().Should().Be(0);
        manager.HasSession(100).Should().BeFalse();
        manager.HasSession(200).Should().BeFalse();
        manager.HasSession(300).Should().BeFalse();
    }

    [Fact]
    public void Dispose_WithMultipleClients_ShouldClearAllPipeClients()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);

        // Act
        manager.Dispose();

        // Assert - pipe clients should be null after dispose
        manager.GetPipeClient(100).Should().BeNull();
        manager.GetPipeClient(200).Should().BeNull();
    }

    [Fact]
    public void Dispose_CalledTwice_ShouldNotThrow()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(12345);

        // Act - double dispose
        manager.Dispose();
        var act = () => manager.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledTwice_ShouldRemainDisposed()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);

        // Act
        manager.Dispose();
        manager.Dispose();

        // Assert - should stay clean
        manager.GetActiveSessionCount().Should().Be(0);
        manager.GetAllSessions().Should().BeEmpty();
    }

    [Fact]
    public void Dispose_WithNoSessions_ShouldNotThrow()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        var act = () => manager.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region PipeConnectedToolBase - Constructor null argument

    [Fact]
    public void PipeConnectedToolBase_WithNullSessionManager_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => new PingTool(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("sessionManager");
    }

    [Fact]
    public void GenericPipeTool_WithNullSessionManager_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => new GenericPipeTool(null!, "test_method");

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("sessionManager");
    }

    #endregion

    #region PipeConnectedToolBase - ParseCommonParams with null arguments

    [Fact]
    public void ParseCommonParams_WithNullArguments_ShouldReturnMissingProcessIdError()
    {
        // Act
        var (processId, elementId, error) = PipeConnectedToolBase.ParseCommonParams(null);

        // Assert
        processId.Should().Be(-1);
        elementId.Should().BeNull();
        error.Should().NotBeNull();
        var errorJson = JsonSerializer.Serialize(error);
        errorJson.Should().Contain("processId");
    }

    #endregion

    #region PipeConnectedToolBase - ParseCommonParams with elementId

    [Fact]
    public void ParseCommonParams_WithElementId_ShouldReturnElementId()
    {
        // Arrange
        var args = ToJsonElement(new { processId = 12345, elementId = "elem_abc" });

        // Act
        var (processId, elementId, error) = PipeConnectedToolBase.ParseCommonParams(args);

        // Assert
        processId.Should().Be(12345);
        elementId.Should().Be("elem_abc");
        error.Should().BeNull();
    }

    [Fact]
    public void ParseCommonParams_WithoutElementId_ShouldReturnNullElementId()
    {
        // Arrange
        var args = ToJsonElement(new { processId = 12345 });

        // Act
        var (processId, elementId, error) = PipeConnectedToolBase.ParseCommonParams(args);

        // Assert
        processId.Should().Be(12345);
        elementId.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void ParseCommonParams_WithOnlyElementId_ShouldReturnMissingProcessIdError()
    {
        // Arrange
        var args = ToJsonElement(new { elementId = "elem_abc" });

        // Act
        var (processId, elementId, error) = PipeConnectedToolBase.ParseCommonParams(args);

        // Assert
        processId.Should().Be(-1);
        elementId.Should().Be("elem_abc");
        error.Should().NotBeNull();
    }

    [Fact]
    public void ParseCommonParams_WithEmptyObject_ShouldReturnMissingProcessIdError()
    {
        // Arrange
        var args = ToJsonElement(new { });

        // Act
        var (processId, _, error) = PipeConnectedToolBase.ParseCommonParams(args);

        // Assert
        processId.Should().Be(-1);
        error.Should().NotBeNull();
    }

    #endregion

    #region PingTool - null arguments

    [Fact]
    public async Task PingTool_Execute_WithNullArguments_ShouldReturnError()
    {
        // Arrange
        var tool = new PingTool(new SessionManager());

        // Act
        var result = await tool.ExecuteAsync(null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    #endregion

    #region GetVisualTreeTool - depth > 100 validation

    [Fact]
    public async Task GetVisualTreeTool_Execute_WithDepthOver100_ShouldReturnError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetVisualTreeTool(sessionManager);
        var parameters = new { processId = 12345, depth = 101 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Depth parameter must be <= 100");
    }

    [Fact]
    public async Task GetVisualTreeTool_Execute_WithDepthExactly100_ShouldNotReturnDepthError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetVisualTreeTool(sessionManager);
        var parameters = new { processId = 12345, depth = 100 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert - depth 100 is valid, so should NOT get depth error
        // (may get pipe error instead since no real pipe exists, but not depth error)
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var errorText = resultJson.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : "";
        errorText.Should().NotContain("Depth parameter must be <= 100");
    }

    [Fact]
    public async Task GetVisualTreeTool_Execute_WithLargeDepth_ShouldReturnError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetVisualTreeTool(sessionManager);
        var parameters = new { processId = 12345, depth = 500 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Depth parameter must be <= 100");
    }

    [Fact]
    public async Task GetVisualTreeTool_Execute_WithNullArguments_ShouldReturnError()
    {
        // Arrange
        var tool = new GetVisualTreeTool(new SessionManager());

        // Act
        var result = await tool.ExecuteAsync(null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    #endregion

    #region GetLogicalTreeTool - depth > 100 validation

    [Fact]
    public async Task GetLogicalTreeTool_Execute_WithDepthOver100_ShouldReturnError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetLogicalTreeTool(sessionManager);
        var parameters = new { processId = 12345, depth = 101 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Depth parameter must be <= 100");
    }

    [Fact]
    public async Task GetLogicalTreeTool_Execute_WithDepthExactly100_ShouldNotReturnDepthError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetLogicalTreeTool(sessionManager);
        var parameters = new { processId = 12345, depth = 100 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert - depth 100 is valid, should not get depth error
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var errorText = resultJson.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : "";
        errorText.Should().NotContain("Depth parameter must be <= 100");
    }

    [Fact]
    public async Task GetLogicalTreeTool_Execute_WithLargeDepth_ShouldReturnError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetLogicalTreeTool(sessionManager);
        var parameters = new { processId = 12345, depth = 999 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Depth parameter must be <= 100");
    }

    [Fact]
    public async Task GetLogicalTreeTool_Execute_WithNullArguments_ShouldReturnError()
    {
        // Arrange
        var tool = new GetLogicalTreeTool(new SessionManager());

        // Act
        var result = await tool.ExecuteAsync(null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    #endregion

    #region GenericPipeTool - DefaultParamExtractor with null arguments

    [Fact]
    public async Task GenericPipeTool_Execute_WithNullArguments_DefaultExtractor_ShouldReturnError()
    {
        // Arrange - use default param extractor (no custom extractor provided)
        var sessionManager = new SessionManager();
        var tool = new GenericPipeTool(sessionManager, "test_method");

        // Act
        var result = await tool.ExecuteAsync(null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    [Fact]
    public async Task GenericPipeTool_Execute_WithCustomExtractorReturningError_ShouldReturnError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        var errorResult = new { success = false, error = "Custom extraction failed" };
        Func<JsonElement?, (int, object?, object?)> customExtractor =
            _ => (-1, null, errorResult);

        var tool = new GenericPipeTool(sessionManager, "test_method", customExtractor);

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Custom extraction failed");
    }

    [Fact]
    public async Task GenericPipeTool_Execute_DefaultExtractor_WithElementId_ShouldPassElementId()
    {
        // Arrange - uses default extractor which parses elementId
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GenericPipeTool(sessionManager, "test_method");
        var parameters = new { processId = 12345, elementId = "elem_xyz" };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert - will get pipe not connected error but should not be processId error
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var errorText = resultJson.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : "";
        errorText.Should().NotContain("processId");
    }

    #endregion

    #region SessionManager - GetIdleSessions edge cases

    [Fact]
    public void GetIdleSessions_WithNoSessions_ShouldReturnEmpty()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        var idleSessions = manager.GetIdleSessions(TimeSpan.FromMinutes(1));

        // Assert
        idleSessions.Should().BeEmpty();
    }

    [Fact]
    public void GetIdleSessions_WithZeroTimeout_ShouldReturnAllSessions()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);

        // Small wait to ensure sessions have non-zero age
        Thread.Sleep(10);

        // Act - zero timeout means all sessions are idle
        var idleSessions = manager.GetIdleSessions(TimeSpan.Zero);

        // Assert
        idleSessions.Should().HaveCount(2);
        idleSessions.Should().Contain(new[] { 100, 200 });
    }

    #endregion

    #region SessionManager - GetAllSessions edge cases

    [Fact]
    public void GetAllSessions_WithNoSessions_ShouldReturnEmpty()
    {
        // Arrange
        var manager = new SessionManager();

        // Act
        var sessions = manager.GetAllSessions();

        // Assert
        sessions.Should().BeEmpty();
    }

    [Fact]
    public void GetAllSessions_AfterDispose_ShouldReturnEmpty()
    {
        // Arrange
        var manager = new SessionManager();
        manager.AddSession(100);
        manager.AddSession(200);
        manager.Dispose();

        // Act
        var sessions = manager.GetAllSessions();

        // Assert
        sessions.Should().BeEmpty();
    }

    #endregion

    #region PipeConnectedToolBase - static helper methods

    [Fact]
    public void CreateMissingParamError_ShouldContainParamName()
    {
        // Arrange & Act
        // Access through ParseCommonParams which internally calls CreateMissingParamError
        var (_, _, error) = PipeConnectedToolBase.ParseCommonParams(null);

        // Assert
        error.Should().NotBeNull();
        var errorJson = JsonSerializer.Serialize(error);
        var doc = JsonDocument.Parse(errorJson);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Contain("Missing required parameter");
    }

    [Fact]
    public async Task CreateNotConnectedError_ShouldContainProcessId()
    {
        // Arrange - we can trigger CreateNotConnectedError through PingTool
        var tool = new PingTool(new SessionManager());
        var parameters = new { processId = 55555 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("55555");
        resultJson.GetProperty("error").GetString().Should().Contain("not connected");
    }

    #endregion

    #region Tree tools - without depth parameter (null depth path)

    [Fact]
    public async Task GetVisualTreeTool_Execute_WithoutDepthParam_ShouldNotReturnDepthError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetVisualTreeTool(sessionManager);
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert - no depth error when depth is not provided
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var errorText = resultJson.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : "";
        errorText.Should().NotContain("Depth parameter must be <= 100");
    }

    [Fact]
    public async Task GetLogicalTreeTool_Execute_WithoutDepthParam_ShouldNotReturnDepthError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetLogicalTreeTool(sessionManager);
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert - no depth error when depth is not provided
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var errorText = resultJson.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : "";
        errorText.Should().NotContain("Depth parameter must be <= 100");
    }

    #endregion

    #region PipeConnectedToolBase - ParseStringParam and ParseIntParam via protected access

    [Fact]
    public async Task GetVisualTreeTool_Execute_WithDepthZero_ShouldNotReturnDepthError()
    {
        // Arrange - depth = 0 exercises ParseIntParam returning a value, and depth <= 100 path
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetVisualTreeTool(sessionManager);
        var parameters = new { processId = 12345, depth = 0 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var errorText = resultJson.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : "";
        errorText.Should().NotContain("Depth parameter must be <= 100");
    }

    #endregion
}
