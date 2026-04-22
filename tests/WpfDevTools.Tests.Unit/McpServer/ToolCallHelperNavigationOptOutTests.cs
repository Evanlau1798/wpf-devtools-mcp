using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public sealed class ToolCallHelperNavigationOptOutTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenNavigationOptOutRequested_ShouldOmitNavigationAndNextSteps()
    {
        var registry = CreateRegistryWithRecommendedStep();
        using var plannerScope = ToolCallHelper.BeginTestScope(
            navigationPlanner: new ToolNavigationPlanner(registry));
        registry.Register("get_binding_errors", _ => ToolNavigationEnvelope.FromRecommended(
            [
                new ToolNextStep(
                    "get_bindings",
                    ToolCallHelper.BuildJsonArgs(("elementId", "TextBox_1"))!.Value,
                    "Inspect the binding declaration directly.",
                    ToolNextStepKind.Diagnostic,
                    1)
            ]));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true, errorCount = 1 }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("navigation", false)),
            CancellationToken.None,
            toolName: "get_binding_errors");

        var structured = result.StructuredContent!.Value;
        structured.GetProperty("success").GetBoolean().Should().BeTrue();
        structured.TryGetProperty("navigation", out _).Should().BeFalse();
        structured.TryGetProperty("nextSteps", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenNavigationOptOutOmitted_ShouldKeepNavigationAndNextSteps()
    {
        var registry = CreateRegistryWithRecommendedStep();
        using var plannerScope = ToolCallHelper.BeginTestScope(
            navigationPlanner: new ToolNavigationPlanner(registry));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true, errorCount = 1 }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            toolName: "known_tool");

        var structured = result.StructuredContent!.Value;
        structured.GetProperty("navigation").GetProperty("recommended")[0].GetProperty("tool").GetString().Should().Be("get_bindings");
        structured.GetProperty("nextSteps")[0].GetProperty("tool").GetString().Should().Be("get_bindings");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenNavigationOptOutRequestedForToolWithoutAdvertisedSupport_ShouldKeepNavigationAndNextSteps()
    {
        var registry = CreateRegistryWithRecommendedStep();
        using var plannerScope = ToolCallHelper.BeginTestScope(
            navigationPlanner: new ToolNavigationPlanner(registry));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true, errorCount = 1 }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("navigation", false)),
            CancellationToken.None,
            toolName: "known_tool");

        var structured = result.StructuredContent!.Value;
        structured.GetProperty("navigation").GetProperty("recommended")[0].GetProperty("tool").GetString().Should().Be("get_bindings");
        structured.GetProperty("nextSteps")[0].GetProperty("tool").GetString().Should().Be("get_bindings");
    }

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }

    private static ToolNavigationRegistry CreateRegistryWithRecommendedStep()
    {
        var registry = new ToolNavigationRegistry();
        registry.Register("known_tool", _ => ToolNavigationEnvelope.FromRecommended(
            [
                new ToolNextStep(
                    "get_bindings",
                    ToolCallHelper.BuildJsonArgs(("elementId", "TextBox_1"))!.Value,
                    "Inspect the binding declaration directly.",
                    ToolNextStepKind.Diagnostic,
                    1)
            ]));
        return registry;
    }
}
