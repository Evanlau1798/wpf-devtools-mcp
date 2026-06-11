using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Gap tests for MCP Server layer to improve code coverage.
/// Covers: PipeConnectedToolBase, PingTool, Tree tools, GenericPipeTool.
/// Note: SessionManager tests are in SessionManagerGapTests.cs
/// </summary>
public class McpServerGapTests
{
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
        resultJson.GetProperty("error").GetString().Should().Contain("depth must be between 0 and 100");
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
        errorText.Should().NotContain("depth must be between 0 and 100");
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
        resultJson.GetProperty("error").GetString().Should().Contain("depth must be between 0 and 100");
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
        resultJson.GetProperty("error").GetString().Should().Contain("depth must be between 0 and 100");
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
        errorText.Should().NotContain("depth must be between 0 and 100");
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
        resultJson.GetProperty("error").GetString().Should().Contain("depth must be between 0 and 100");
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
        Func<SessionManager, JsonElement?, (int, object?, object?)> customExtractor =
            (_, _) => (-1, null, errorResult);

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

        // Assert - will get pipe not connected error but should not be "missing processId" error
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var errorText = resultJson.TryGetProperty("error", out var errorProp) ? errorProp.GetString() : "";
        errorText.Should().NotContain("Missing required parameter: processId");
        errorText.Should().Contain("Named pipe not connected"); // Should be pipe connection error, not parameter error
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
        errorText.Should().NotContain("depth must be between 0 and 100");
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
        errorText.Should().NotContain("depth must be between 0 and 100");
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
        errorText.Should().NotContain("depth must be between 0 and 100");
    }

    #endregion
}

