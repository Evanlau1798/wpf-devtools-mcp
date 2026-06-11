using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public sealed class SceneDiagnosticsContractTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }

    [Fact]
    public void GetStateDiff_ShouldExposeSnapshotIdAndOptionalTrigger()
    {
        var method = typeof(SceneDiagnosticsMcpTools).GetMethod(nameof(SceneDiagnosticsMcpTools.GetStateDiff));

        method.Should().NotBeNull();

        var snapshotId = method!.GetParameters().Single(parameter => parameter.Name == "snapshotId");
        snapshotId.ParameterType.Should().Be(typeof(string));
        snapshotId.HasDefaultValue.Should().BeFalse();

        var processId = method.GetParameters().Single(parameter => parameter.Name == "processId");
        processId.ParameterType.Should().Be(typeof(int?));
        processId.HasDefaultValue.Should().BeTrue();
        processId.DefaultValue.Should().BeNull();

        var trigger = method.GetParameters().Single(parameter => parameter.Name == "trigger");
        trigger.ParameterType.Should().Be(typeof(string));
        trigger.HasDefaultValue.Should().BeTrue();
        trigger.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void GetElementSnapshot_ShouldExposeRequiredElementIdAndOptionalProcessId()
    {
        var method = typeof(SceneDiagnosticsMcpTools).GetMethod(nameof(SceneDiagnosticsMcpTools.GetElementSnapshot));

        method.Should().NotBeNull();

        var elementId = method!.GetParameters().Single(parameter => parameter.Name == "elementId");
        elementId.ParameterType.Should().Be(typeof(string));
        elementId.HasDefaultValue.Should().BeFalse();

        var processId = method.GetParameters().Single(parameter => parameter.Name == "processId");
        processId.ParameterType.Should().Be(typeof(int?));
        processId.HasDefaultValue.Should().BeTrue();
        processId.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void DiagnoseVisibility_ShouldExposeRequiredElementIdAndOptionalProcessId()
    {
        var method = typeof(SceneDiagnosticsMcpTools).GetMethod(nameof(SceneDiagnosticsMcpTools.DiagnoseVisibility));

        method.Should().NotBeNull();

        var elementId = method!.GetParameters().Single(parameter => parameter.Name == "elementId");
        elementId.ParameterType.Should().Be(typeof(string));
        elementId.HasDefaultValue.Should().BeFalse();

        var processId = method.GetParameters().Single(parameter => parameter.Name == "processId");
        processId.ParameterType.Should().Be(typeof(int?));
        processId.HasDefaultValue.Should().BeTrue();
        processId.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void GetInteractionReadiness_ShouldExposeRequiredElementIdAndOptionalArguments()
    {
        var method = typeof(SceneDiagnosticsMcpTools).GetMethod(nameof(SceneDiagnosticsMcpTools.GetInteractionReadiness));

        method.Should().NotBeNull();

        var elementId = method!.GetParameters().Single(parameter => parameter.Name == "elementId");
        elementId.ParameterType.Should().Be(typeof(string));
        elementId.HasDefaultValue.Should().BeFalse();

        var processId = method.GetParameters().Single(parameter => parameter.Name == "processId");
        processId.ParameterType.Should().Be(typeof(int?));
        processId.HasDefaultValue.Should().BeTrue();
        processId.DefaultValue.Should().BeNull();

        var interactionType = method.GetParameters().Single(parameter => parameter.Name == "interactionType");
        interactionType.ParameterType.Should().Be(typeof(string));
        interactionType.HasDefaultValue.Should().BeTrue();
        interactionType.DefaultValue.Should().Be("Click");
    }

    [Fact]
    public void GetUiSummary_ShouldExposeOptionalElementIdDepthDepthModeAndSummaryOnly()
    {
        var method = typeof(SceneDiagnosticsMcpTools).GetMethod(nameof(SceneDiagnosticsMcpTools.GetUiSummary));

        method.Should().NotBeNull();

        var elementId = method!.GetParameters().Single(parameter => parameter.Name == "elementId");
        elementId.ParameterType.Should().Be(typeof(string));
        elementId.HasDefaultValue.Should().BeTrue();
        elementId.DefaultValue.Should().BeNull();

        var processId = method.GetParameters().Single(parameter => parameter.Name == "processId");
        processId.ParameterType.Should().Be(typeof(int?));
        processId.HasDefaultValue.Should().BeTrue();
        processId.DefaultValue.Should().BeNull();

        var depth = method.GetParameters().Single(parameter => parameter.Name == "depth");
        depth.ParameterType.Should().Be(typeof(int?));
        depth.HasDefaultValue.Should().BeTrue();
        depth.DefaultValue.Should().BeNull();

        var depthMode = method.GetParameters().Single(parameter => parameter.Name == "depthMode");
        depthMode.ParameterType.Should().Be(typeof(string));
        depthMode.HasDefaultValue.Should().BeTrue();
        depthMode.DefaultValue.Should().BeNull();

        var summaryOnly = method.GetParameters().Single(parameter => parameter.Name == "summaryOnly");
        summaryOnly.ParameterType.Should().Be(typeof(bool));
        summaryOnly.HasDefaultValue.Should().BeTrue();
        summaryOnly.DefaultValue.Should().Be(false);
    }

    [Fact]
    public void GetFormSummary_ShouldExposeOptionalElementIdProcessIdAndIncludeFramework()
    {
        var method = typeof(SceneDiagnosticsMcpTools).GetMethod(nameof(SceneDiagnosticsMcpTools.GetFormSummary));

        method.Should().NotBeNull();

        var elementId = method!.GetParameters().Single(parameter => parameter.Name == "elementId");
        elementId.ParameterType.Should().Be(typeof(string));
        elementId.HasDefaultValue.Should().BeTrue();
        elementId.DefaultValue.Should().BeNull();

        var processId = method.GetParameters().Single(parameter => parameter.Name == "processId");
        processId.ParameterType.Should().Be(typeof(int?));
        processId.HasDefaultValue.Should().BeTrue();
        processId.DefaultValue.Should().BeNull();

        var includeFramework = method.GetParameters().Single(parameter => parameter.Name == "includeFramework");
        includeFramework.ParameterType.Should().Be(typeof(bool));
        includeFramework.HasDefaultValue.Should().BeTrue();
        includeFramework.DefaultValue.Should().Be(false);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithCollapsedRootCause_ShouldSuggestSetVisibility()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                elementId = "Text_1",
                isUserVisible = false,
                rootCause = "Element Visibility=Collapsed."
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "Text_1")),
            CancellationToken.None,
            toolName: "diagnose_visibility");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("set_dp_value");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("Text_1");
        nextSteps[0].GetProperty("params").GetProperty("propertyName").GetString().Should().Be("Visibility");
        nextSteps[0].GetProperty("params").GetProperty("value").GetString().Should().Be("Visible");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithAncestorVisibilityRootCause_ShouldPreferDpInspection()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                elementId = "Text_2",
                isUserVisible = false,
                rootCause = "Ancestor HiddenPanel has Visibility=Collapsed."
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "Text_2")),
            CancellationToken.None,
            toolName: "diagnose_visibility");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_dp_value_source");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("Text_2");
        nextSteps[0].GetProperty("params").GetProperty("propertyName").GetString().Should().Be("Visibility");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithOpacityRootCause_ShouldSuggestOpacityReset()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                elementId = "Text_3",
                isUserVisible = false,
                rootCause = "Element Opacity=0 makes it transparent."
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "Text_3")),
            CancellationToken.None,
            toolName: "diagnose_visibility");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("set_dp_value");
        nextSteps[0].GetProperty("params").GetProperty("propertyName").GetString().Should().Be("Opacity");
        nextSteps[0].GetProperty("params").GetProperty("value").GetDouble().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithRenderTransformOffscreenRootCause_ShouldNotSuggestMutationNavigation()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                elementId = "Button_1",
                isUserVisible = false,
                rootCause = "Element Button_1 is outside the visible viewport after applying its RenderTransform."
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "Button_1")),
            CancellationToken.None,
            toolName: "diagnose_visibility");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithFormCommandBlocker_ShouldSuggestMostActionableControl()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                formScope = "Form_1",
                commands = new[]
                {
                    new { elementId = "SaveButton", blockers = new[] { "CommandCannotExecute" } }
                },
                summary = new
                {
                    totalInputs = 2,
                    emptyInputs = 1,
                    errorCount = 1,
                    validationSubmittable = false,
                    interactionSubmittable = false,
                    isSubmittable = false
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "Form_1")),
            CancellationToken.None,
            toolName: "get_form_summary");

        var summary = result.StructuredContent!.Value.GetProperty("summary");
        summary.GetProperty("validationSubmittable").GetBoolean().Should().BeFalse();
        summary.GetProperty("interactionSubmittable").GetBoolean().Should().BeFalse();

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_commands");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("SaveButton");
    }
}
