using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolExecutionPolicyBatchSnapshotTests
{
    [Fact]
    public void EvaluateToolCall_WhenBatchInfersSnapshotAndSensitiveReadsAreDisabled_ShouldDeny()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "true",
            allowViewModelInspection: "true");

        var decision = policy.EvaluateToolCall(
            "batch_mutate",
            ToArguments("""{"captureSnapshot":true,"mutations":[{"tool":"click_element","args":{}}]}"""));

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("SecurityError");
        decision.PolicyCategory.Should().Be("sensitive-reads");
    }

    [Fact]
    public void EvaluateToolCall_WhenBatchInfersSnapshotAndSensitiveReadsAreEnabled_ShouldAllow()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "true",
            allowViewModelInspection: "true",
            allowSensitiveReads: "true");

        policy.EvaluateToolCall(
                "batch_mutate",
                ToArguments("""{"captureSnapshot":true,"mutations":[{"tool":"click_element","args":{}}]}"""))
            .IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("null")]
    public void EvaluateToolCall_WhenBatchSnapshotIsDisabled_ShouldNotRequireSensitiveReads(string captureSnapshot)
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "true",
            allowViewModelInspection: "true");

        policy.EvaluateToolCall(
                "batch_mutate",
                ToArguments(
                    """{"captureSnapshot":CAPTURE_SNAPSHOT,"mutations":[{"tool":"click_element","args":{}}]}"""
                        .Replace("CAPTURE_SNAPSHOT", captureSnapshot, StringComparison.Ordinal)))
            .IsAllowed.Should().BeTrue();
    }

    private static Dictionary<string, JsonElement> ToArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }
}
