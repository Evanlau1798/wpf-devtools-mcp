using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ProcessEnvironment")]
public sealed class McpToolExecutionPolicyPreviewDiagnosticsTests
{
    [Theory]
    [InlineData("{\"includeRuntimeDiagnostics\":true}")]
    [InlineData("{\"includeRuntimeDiagnostics\":\"true\"}")]
    [InlineData("{\"includeScreenshotDiagnostics\":true}")]
    public void EvaluateToolCall_WhenPreviewDiagnosticsRequestSensitiveReadsAndGateIsDisabled_ShouldDeny(string argumentsJson)
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "true",
            allowViewModelInspection: "true");

        var decision = policy.EvaluateToolCall("preview_ui_blueprint", ToArguments(argumentsJson));

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("SecurityError");
        decision.PolicyCategory.Should().Be("sensitive-reads");
    }

    [Fact]
    public void EvaluateToolCall_WhenPreviewScreenshotDiagnosticsRequestScreenshotAndGateIsDisabled_ShouldDeny()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "false",
            allowViewModelInspection: "true",
            allowSensitiveReads: "true");

        var decision = policy.EvaluateToolCall(
            "preview_ui_blueprint",
            ToArguments("{\"includeScreenshotDiagnostics\":true}"));

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("SecurityError");
        decision.PolicyCategory.Should().Be("screenshots");
    }

    [Fact]
    public void EvaluateToolCall_WhenPreviewDiagnosticsAreNotRequested_ShouldAllowWithoutSensitiveReadGate()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "false",
            allowViewModelInspection: "true");

        policy.EvaluateToolCall(
                "preview_ui_blueprint",
                ToArguments("{\"includeRuntimeDiagnostics\":false,\"includeScreenshotDiagnostics\":false}"))
            .IsAllowed.Should().BeTrue();
    }

    private static Dictionary<string, JsonElement> ToArguments(string argumentsJson)
    {
        using var document = JsonDocument.Parse(argumentsJson);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
