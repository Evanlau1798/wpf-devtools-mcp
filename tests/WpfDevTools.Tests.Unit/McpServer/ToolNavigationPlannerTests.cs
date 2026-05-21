using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public sealed class ToolNavigationPlannerTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
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

                using var plannerScope = ToolCallHelper.BeginTestScope(
                    navigationPlanner: new ToolNavigationPlanner(registry));
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
        nextSteps[0].TryGetProperty("whyNow", out var whyNow).Should().BeTrue();
        whyNow.GetString().Should().NotBeNullOrWhiteSpace();
        nextSteps[0].TryGetProperty("confidence", out var confidence).Should().BeTrue();
        confidence.GetString().Should().Be("high");
        capturedContext.Should().NotBeNull();
        capturedContext!.ToolName.Should().Be("known_tool");
        capturedContext.Payload.GetProperty("errorCount").GetInt32().Should().Be(1);
        capturedContext.Arguments.Should().NotBeNull();
        capturedContext.Arguments!.Value.GetProperty("processId").GetInt32().Should().Be(12345);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithUnknownToolPlanner_ShouldFallbackToEmptyNextSteps()
    {
        using var plannerScope = ToolCallHelper.BeginTestScope(
            navigationPlanner: new ToolNavigationPlanner(new ToolNavigationRegistry()));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true }),
            null,
            CancellationToken.None,
            toolName: "unknown_tool");

        result.StructuredContent!.Value.GetProperty("nextSteps").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenNestedPlannerScopeDisposes_ShouldRestoreOuterPlanner()
    {
        var outerRegistry = new ToolNavigationRegistry();
        outerRegistry.Register("known_tool", _ =>
        [
            new ToolNextStep("outer_step", NavigationParamBuilders.Create(), "Outer step", ToolNextStepKind.Diagnostic, 1)
        ]);

        using var outerScope = ToolCallHelper.BeginTestScope(
            navigationPlanner: new ToolNavigationPlanner(outerRegistry));

        var innerRegistry = new ToolNavigationRegistry();
        innerRegistry.Register("known_tool", _ =>
        [
            new ToolNextStep("inner_step", NavigationParamBuilders.Create(), "Inner step", ToolNextStepKind.Diagnostic, 1)
        ]);

        using (ToolCallHelper.BeginTestScope(
            navigationPlanner: new ToolNavigationPlanner(innerRegistry)))
        {
            var innerResult = await ToolCallHelper.ExecuteAndWrapAsync(
                (_, _) => Task.FromResult<object>(new { success = true }),
                null,
                CancellationToken.None,
                toolName: "known_tool");

            innerResult.StructuredContent!.Value.GetProperty("nextSteps")[0].GetProperty("tool").GetString().Should().Be("inner_step");
        }

        var outerResult = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true }),
            null,
            CancellationToken.None,
            toolName: "known_tool");

        outerResult.StructuredContent!.Value.GetProperty("nextSteps")[0].GetProperty("tool").GetString().Should().Be("outer_step");
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

    [Theory]
    [InlineData("clear_dp_value")]
    [InlineData("wait_for_dp_change_after_mutation")]
    [InlineData("force_binding_update")]
    [InlineData("focus_element")]
    [InlineData("drag_and_drop")]
    [InlineData("scroll_to_element")]
    [InlineData("simulate_keyboard")]
    [InlineData("override_style_setter")]
    [InlineData("invalidate_layout")]
    public async Task ExecuteAndWrapAsync_WithSnapshotAwareMutationTool_ShouldRecommendStateDiff(string toolName)
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new { success = true }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "Target_1"), ("propertyName", "Text")),
            CancellationToken.None,
            navigationState: new NavigationSessionState("snapshot_123", null),
            toolName: toolName);

        var navigation = result.StructuredContent!.Value.GetProperty("navigation");
        var recommendedTools = navigation
            .GetProperty("recommended")
            .EnumerateArray()
            .Select(step => step.GetProperty("tool").GetString())
            .ToArray();

        recommendedTools.Should().Contain("get_state_diff");
        navigation.GetProperty("contextRefs")[0].GetProperty("snapshotId").GetString().Should().Be("snapshot_123");
        navigation.GetProperty("prefetchTools").EnumerateArray().Select(item => item.GetString()).Should().Contain("restore_state_snapshot");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithSuccessfulBatchMutationSnapshot_ShouldRecommendRollbackAndVerification()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_123",
                stateDiff = new
                {
                    success = true,
                    propertyChanges = new[] { new { elementId = "NameTextBox", propertyName = "Text" } }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "NameTextBox")),
            CancellationToken.None,
            toolName: "batch_mutate");

        var navigation = result.StructuredContent!.Value.GetProperty("navigation");
        var recommendedTools = navigation
            .GetProperty("recommended")
            .EnumerateArray()
            .Select(step => step.GetProperty("tool").GetString())
            .ToArray();

        recommendedTools.Should().Contain("restore_state_snapshot");
        recommendedTools.Should().Contain("get_ui_summary");
        navigation.GetProperty("contextRefs")[0].GetProperty("type").GetString().Should().Be("mutation-session");
        navigation.GetProperty("contextRefs")[0].GetProperty("snapshotId").GetString().Should().Be("snapshot_123");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithSuccessfulConnect_ShouldRecommendSceneFirstSummary()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                processId = 12345,
                processName = "TestApp"
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            toolName: "connect");

        var navigation = result.StructuredContent!.Value.GetProperty("navigation");
        var firstStep = navigation.GetProperty("recommended")[0];
        firstStep.GetProperty("tool").GetString().Should().Be("get_ui_summary");
        firstStep.GetProperty("params").GetProperty("processId").GetInt32().Should().Be(12345);
        firstStep.GetProperty("whyNow").GetString().Should().Contain("scene");

        navigation
            .GetProperty("alternatives")
            .EnumerateArray()
            .Select(step => step.GetProperty("tool").GetString())
            .Should().NotContain("get_element_snapshot",
                "post-connect navigation should only advertise directly executable follow-ups unless an elementId is known");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithAmbiguousConnect_ShouldRecommendProcessListing()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = false,
                errorCode = "MultipleWpfProcessesFound",
                candidateCount = 2
            }),
            ToolCallHelper.BuildJsonArgs(("windowFilter", "foreground")),
            CancellationToken.None,
            toolName: "connect");

        var navigation = result.StructuredContent!.Value.GetProperty("navigation");
        var firstStep = navigation.GetProperty("recommended")[0];
        firstStep.GetProperty("tool").GetString().Should().Be("get_processes");
        firstStep.GetProperty("params").GetProperty("windowFilter").GetString().Should().Be("foreground");
        firstStep.GetProperty("whyNow").GetString().Should().Contain("multiple");
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

    [Fact]
    public async Task ExecuteAndWrapAsync_WithAffectedElementsResult_ShouldRecommendBindingVerification()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                confidence = "best-effort",
                matchStrategy = "simple-path-match",
                requiresVerification = true,
                affectedCount = 1,
                affectedElements = new[]
                {
                    new
                    {
                        elementId = "NameTextBox_1",
                        elementType = "TextBox",
                        elementName = "NameTextBox",
                        propertyName = "Text",
                        bindingPath = "Name"
                    }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "RootPanel_1"), ("propertyName", "Name"), ("recursive", true)),
            CancellationToken.None,
            toolName: "get_affected_elements");

        var navigation = result.StructuredContent!.Value.GetProperty("navigation");
        navigation.GetProperty("recommended")[0].GetProperty("tool").GetString().Should().Be("get_bindings");
        navigation.GetProperty("recommended")[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("RootPanel_1");
        navigation.GetProperty("recommended")[0].GetProperty("params").GetProperty("recursive").GetBoolean().Should().BeTrue();
        navigation.GetProperty("alternatives")[0].GetProperty("tool").GetString().Should().Be("get_element_snapshot");
    }
}
