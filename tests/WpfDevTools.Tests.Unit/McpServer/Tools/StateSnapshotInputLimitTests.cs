using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class StateSnapshotInputLimitTests
{
    [Fact]
    public async Task CaptureStateSnapshot_WithTooManyDependencyPropertyNames_ShouldReturnInvalidArgument()
    {
        using var sessionManager = new SessionManager();
        var tool = new CaptureStateSnapshotTool(sessionManager);
        var propertyNames = Enumerable
            .Range(0, BatchItemLimits.MaxQueryInputItems + 1)
            .Select(index => $"Property{index}")
            .ToArray();

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 51050,
            propertyNames
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("error").GetString().Should().Contain("propertyNames");
        result.GetProperty("errorData").GetProperty("maxItems").GetInt32()
            .Should().Be(BatchItemLimits.MaxQueryInputItems);
    }

    [Fact]
    public async Task CaptureStateSnapshot_WithTooLongViewModelPropertyName_ShouldReturnInvalidArgument()
    {
        using var sessionManager = new SessionManager();
        var tool = new CaptureStateSnapshotTool(sessionManager);
        var longPropertyName = new string('A', 257);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 51051,
            viewModelPropertyNames = new[] { longPropertyName }
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("error").GetString().Should().Contain("viewModelPropertyNames");
        result.GetProperty("errorData").GetProperty("maxLength").GetInt32().Should().Be(256);
    }

    [Fact]
    public async Task CaptureStateSnapshot_WithWhitespacePaddedTooLongPropertyName_ShouldReturnInvalidArgument()
    {
        using var sessionManager = new SessionManager();
        var tool = new CaptureStateSnapshotTool(sessionManager);
        var paddedPropertyName = new string(' ', 257) + "Text";

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 51052,
            propertyNames = new[] { paddedPropertyName }
        }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("error").GetString().Should().Contain("propertyNames");
        result.GetProperty("errorData").GetProperty("actualLength").GetInt32()
            .Should().Be(paddedPropertyName.Length);
    }
}
