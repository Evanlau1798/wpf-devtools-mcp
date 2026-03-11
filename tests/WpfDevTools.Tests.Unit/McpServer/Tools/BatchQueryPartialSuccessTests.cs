using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class BatchQueryPartialSuccessTests
{
    [Fact]
    public async Task ExecuteAsync_WithMixedSuccessAndFailure_ShouldReportCounts()
    {
        var result = await BatchQueryExecutor.ExecuteAsync(
            new[] { "Button_1", "Missing_1" },
            new[] { "Width" },
            (elementId, propertyName, _) => Task.FromResult<object>(
                elementId == "Missing_1"
                    ? new { success = false, error = "Element not found" }
                    : new { success = true, propertyName, currentValue = 120 }),
            CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("resultCount").GetInt32().Should().Be(2);
        json.GetProperty("successCount").GetInt32().Should().Be(1);
        json.GetProperty("failureCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithMixedSuccessAndFailure_ShouldTreatBatchAsSuccessful()
    {
        var result = await BatchQueryExecutor.ExecuteAsync(
            new[] { "Button_1", "Missing_1" },
            new[] { "Width" },
            (elementId, propertyName, _) => Task.FromResult<object>(
                elementId == "Missing_1"
                    ? new { success = false, error = "Element not found" }
                    : new { success = true, propertyName, currentValue = 120 }),
            CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);

        json.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
