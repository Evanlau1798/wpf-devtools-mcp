using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ToolNavigationPlannerTests : IDisposable
{
    public void Dispose()
    {
        ToolCallHelper.ResetCacheForTesting();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithKnownToolPlanner_ShouldEmbedPlannerNextSteps()
    {
        ToolNavigationContext? capturedContext = null;
        var registry = new ToolNavigationRegistry();
        registry.Register("known_tool", context =>
        {
            capturedContext = context;
            return
            [
                new ToolNextStep(
                    "get_bindings",
                    NavigationParamBuilders.Create(("elementId", "TextBox_1")),
                    "Inspect the binding declaration for the current element.",
                    ToolNextStepKind.Diagnostic,
                    1)
            ];
        });

        ToolCallHelper.SetNavigationPlannerForTesting(new ToolNavigationPlanner(registry));
        var args = ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "TextBox_1"));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (input, _) => Task.FromResult<object>(new { success = true, errorCount = 1 }),
            args,
            CancellationToken.None,
            toolName: "known_tool");

        result.IsError.Should().BeFalse();
        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_bindings");
        capturedContext.Should().NotBeNull();
        capturedContext!.ToolName.Should().Be("known_tool");
        capturedContext.Payload.GetProperty("errorCount").GetInt32().Should().Be(1);
        capturedContext.Arguments.Should().NotBeNull();
        capturedContext.Arguments!.Value.GetProperty("processId").GetInt32().Should().Be(12345);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithUnknownToolPlanner_ShouldFallbackToEmptyNextSteps()
    {
        ToolCallHelper.SetNavigationPlannerForTesting(new ToolNavigationPlanner(new ToolNavigationRegistry()));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true }),
            null,
            CancellationToken.None,
            toolName: "unknown_tool");

        result.StructuredContent!.Value.GetProperty("nextSteps").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Plan_WithSameInput_ShouldReturnStableOrderedSuggestions()
    {
        var registry = new ToolNavigationRegistry();
        registry.Register("known_tool", _ =>
        [
            new ToolNextStep("second", NavigationParamBuilders.Create(), "Second step", ToolNextStepKind.Diagnostic, 2),
            new ToolNextStep("first", NavigationParamBuilders.Create(), "First step", ToolNextStepKind.Diagnostic, 1)
        ]);

        var planner = new ToolNavigationPlanner(registry);
        var payload = JsonSerializer.SerializeToElement(new { success = true });

        var first = planner.Plan("known_tool", payload, null);
        var second = planner.Plan("known_tool", payload, null);

        first.Select(step => step.Tool).Should().Equal("first", "second");
        second.Select(step => step.Tool).Should().Equal("first", "second");
    }

    [Fact]
    public void Plan_ShouldDropSelfReferentialRecommendations_ToAvoidLoops()
    {
        var registry = new ToolNavigationRegistry();
        registry.Register("known_tool", _ =>
        [
            new ToolNextStep("known_tool", NavigationParamBuilders.Create(), "Loop", ToolNextStepKind.Diagnostic, 1),
            new ToolNextStep("get_bindings", NavigationParamBuilders.Create(), "Inspect bindings", ToolNextStepKind.Diagnostic, 2)
        ]);

        var planner = new ToolNavigationPlanner(registry);
        var payload = JsonSerializer.SerializeToElement(new { success = true });

        var result = planner.Plan("known_tool", payload, null);

        result.Select(step => step.Tool).Should().Equal("get_bindings");
    }

    [Fact]
    public void Plan_WithNavigationState_ShouldExposeSessionFactsToHandler()
    {
        NavigationSessionState? capturedState = null;
        var registry = new ToolNavigationRegistry();
        registry.Register("known_tool", context =>
        {
            capturedState = context.SessionState;
            return Array.Empty<ToolNextStep>();
        });

        var planner = new ToolNavigationPlanner(registry);
        var payload = JsonSerializer.SerializeToElement(new { success = true });
        var state = new NavigationSessionState(
            "snapshot_123",
            new ActiveTraceNavigationState("Click", "Button_1", DateTimeOffset.UtcNow));

        _ = planner.Plan("known_tool", payload, null, state);

        capturedState.Should().BeEquivalentTo(state);
    }
}
