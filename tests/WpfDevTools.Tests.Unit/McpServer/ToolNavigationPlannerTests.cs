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

    [Fact]
    public void PlanEnvelope_ShouldPreserveRecommendedAlternativeAndContextRefBranches()
    {
        var registry = new ToolNavigationRegistry();
        registry.Register("known_tool", _ => new ToolNavigationEnvelope(
            [
                new ToolNextStep("get_datacontext_chain", NavigationParamBuilders.Create(("elementId", "TextBox_1")), "Inspect DataContext.", ToolNextStepKind.Diagnostic, 1)
            ],
            [
                new ToolNextStep("get_bindings", NavigationParamBuilders.Create(("elementId", "TextBox_1")), "Inspect binding declaration.", ToolNextStepKind.Diagnostic, 2)
            ],
            ["get_bindings"],
            [
                ToolNavigationReference.Create(
                    "binding-issue",
                    ("elementId", "TextBox_1"),
                    ("propertyName", "Text"),
                    ("diagnosis", "PathMismatch"))
            ]));

        var planner = new ToolNavigationPlanner(registry);
        var envelope = planner.PlanEnvelope("known_tool", JsonSerializer.SerializeToElement(new { success = true }), null);

        envelope.Recommended[0].Tool.Should().Be("get_datacontext_chain");
        envelope.Alternatives[0].Tool.Should().Be("get_bindings");
        envelope.PrefetchTools.Should().Equal("get_bindings");
        envelope.ContextRefs[0].Type.Should().Be("binding-issue");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithVisibilityCue_ShouldEmitVisibilityContextRef()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                nodes = new[]
                {
                    new
                    {
                        elementId = "Text_1",
                        annotations = new[] { "visibility:collapsed" }
                    }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            toolName: "get_ui_summary");

        var contextRef = result.StructuredContent!.Value
            .GetProperty("navigation")
            .GetProperty("contextRefs")[0];
        contextRef.GetProperty("type").GetString().Should().Be("visibility-issue");
        contextRef.GetProperty("elementId").GetString().Should().Be("Text_1");
        contextRef.TryGetProperty("rootCause", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithSnapshotAwareAction_ShouldEmitMutationSessionContextRef()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true, clicked = true }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "SaveButton")),
            CancellationToken.None,
            navigationState: new NavigationSessionState("snapshot_123", null),
            toolName: "click_element");

        var navigation = result.StructuredContent!.Value.GetProperty("navigation");
        navigation.GetProperty("contextRefs")[0].GetProperty("type").GetString().Should().Be("mutation-session");
        navigation.GetProperty("contextRefs")[0].GetProperty("snapshotId").GetString().Should().Be("snapshot_123");
        navigation.GetProperty("prefetchTools").EnumerateArray().Select(item => item.GetString()).Should().Contain("restore_state_snapshot");
    }

    [Fact]
    public void PlanEnvelope_ShouldDeriveCompactPrefetchToolsFromRecommendedAndAlternatives()
    {
        var registry = new ToolNavigationRegistry();
        registry.Register("known_tool", _ => new ToolNavigationEnvelope(
            [
                new ToolNextStep(
                    "get_state_diff",
                    NavigationParamBuilders.Create(("snapshotId", "snapshot_123")),
                    "Compare state changes.",
                    ToolNextStepKind.Verification,
                    1,
                    PrefetchTools: ["restore_state_snapshot"])
            ],
            [
                new ToolNextStep(
                    "get_bindings",
                    NavigationParamBuilders.Create(("elementId", "TextBox_1")),
                    "Inspect bindings.",
                    ToolNextStepKind.Diagnostic,
                    2)
            ],
            [],
            []));

        var planner = new ToolNavigationPlanner(registry);
        var envelope = planner.PlanEnvelope("known_tool", JsonSerializer.SerializeToElement(new { success = true }), null);

        envelope.PrefetchTools.Should().Equal("restore_state_snapshot", "get_bindings");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ContextRefs_ShouldRemainDescriptiveOnly()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_123",
                snapshotSummary = new { dependencyPropertyCount = 1 }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("propertyNames", new[] { "Width" })),
            CancellationToken.None,
            toolName: "capture_state_snapshot");

        var contextRef = result.StructuredContent!.Value
            .GetProperty("navigation")
            .GetProperty("contextRefs")[0];
        contextRef.TryGetProperty("handleId", out _).Should().BeFalse();
        contextRef.TryGetProperty("serverHandle", out _).Should().BeFalse();
        contextRef.GetProperty("type").GetString().Should().Be("mutation-session");
    }
}
