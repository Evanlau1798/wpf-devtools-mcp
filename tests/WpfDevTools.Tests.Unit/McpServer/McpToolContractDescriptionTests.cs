using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolContractDescriptionTests
{
    private static readonly (Type ToolType, string MethodName)[] RuntimeNavigationTools =
    [
        (typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingErrors)),
        (typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingMismatches)),
        (typeof(MvvmMcpTools), nameof(MvvmMcpTools.GetValidationErrors)),
        (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.DiagnoseVisibility)),
        (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetInteractionReadiness)),
        (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetFormSummary)),
        (typeof(InteractionMcpTools), nameof(InteractionMcpTools.ClickElement)),
        (typeof(MvvmMcpTools), nameof(MvvmMcpTools.ExecuteCommand)),
        (typeof(MvvmMcpTools), nameof(MvvmMcpTools.ModifyViewModel)),
        (typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.SetDpValue)),
        (typeof(EventMcpTools), nameof(EventMcpTools.FireRoutedEvent)),
        (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetUiSummary)),
        (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetElementSnapshot)),
        (typeof(StateMcpTools), nameof(StateMcpTools.CaptureStateSnapshot)),
        (typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetStateDiff))
    ];

    [Theory]
    [InlineData(typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.SetDpValue), "requestedValue")]
    [InlineData(typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.ClearDpValue), "hadLocalValue")]
    [InlineData(typeof(StyleMcpTools), nameof(StyleMcpTools.OverrideStyleSetter), "oldValue")]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.ExecuteCommand), "commandName")]
    [InlineData(typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.GetDpValueSource), "rawBaseValueSource")]
    [InlineData(typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingValueChain), "LocalDataContext")]
    [InlineData(typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.GetRenderStats), "isWarmedUp")]
    [InlineData(typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.GetRenderStats), "confidence")]
    [InlineData(typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.GetRenderStats), "warmUp")]
    [InlineData(typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.FindBindingLeaks), "potentialLeaks")]
    [InlineData(typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.FindBindingLeaks), "samplingDurationMs")]
    [InlineData(typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.FindBindingLeaks), "warmUp")]
    [InlineData(typeof(TreeMcpTools), nameof(TreeMcpTools.GetWindows), "isMainWindow")]
    [InlineData(typeof(TreeMcpTools), nameof(TreeMcpTools.GetWindows), "index, title, type, isActive, isVisible, isMainWindow, elementId")]
    [InlineData(typeof(TreeMcpTools), nameof(TreeMcpTools.GetNamescope), "inactive tabs")]
    [InlineData(typeof(EventMcpTools), nameof(EventMcpTools.GetEventHandlers), "mayBeIncomplete")]
    [InlineData(typeof(InteractionMcpTools), nameof(InteractionMcpTools.SimulateKeyboard), "focusChanged")]
    [InlineData(typeof(StateMcpTools), nameof(StateMcpTools.RestoreStateSnapshot), "skippedViewModelPropertyCount")]
    [InlineData(typeof(StateMcpTools), nameof(StateMcpTools.RestoreStateSnapshot), "skippedViewModelProperties")]
    [InlineData(typeof(StateMcpTools), nameof(StateMcpTools.RestoreStateSnapshot), "restoredDependencyProperties")]
    [InlineData(typeof(StateMcpTools), nameof(StateMcpTools.RestoreStateSnapshot), "restoredViewModelProperties")]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.GetViewModel), "canWrite")]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.ModifyViewModel), "requestedValueType")]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.GetValidationErrors), "logical and visual descendants")]
    [InlineData(typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingErrors), "validation rule errors")]
    [InlineData(typeof(StyleMcpTools), nameof(StyleMcpTools.GetAppliedStyles), "localResourceReferences")]
    [InlineData(typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetUiSummary), "summaryOnly")]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "canConnectFromCurrentServer")]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "connectionWarning")]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "suggestedAction")]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "requiresElevationToConnect")]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "autoDiscovered")]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "selectionStrategy")]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "largest_working_set")]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "windowFilter")]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "windowFilter")]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "foreground")]
    public void ToolDescriptions_ShouldMentionUpdatedContractTerms(Type toolType, string methodName, string expectedTerm)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var description = method!.GetCustomAttribute<DescriptionAttribute>();
        description.Should().NotBeNull();
        description!.Description.Should().Contain(expectedTerm);
    }

    [Fact]
    public void RuntimeNavigationToolDescriptions_ShouldKeepNextStepsOutOfPayloadExamples()
    {
        foreach (var (toolType, methodName) in RuntimeNavigationTools)
        {
            var description = GetDescription(toolType, methodName);

            description.Should().NotContain("\"nextSteps\"");
            description.Should().NotContain("nextSteps: [");
        }
    }

    [Theory]
    [InlineData(typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.SetDpValue))]
    [InlineData(typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.ClearDpValue))]
    [InlineData(typeof(StyleMcpTools), nameof(StyleMcpTools.OverrideStyleSetter))]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.ExecuteCommand))]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.ModifyViewModel))]
    [InlineData(typeof(InteractionMcpTools), nameof(InteractionMcpTools.ClickElement))]
    [InlineData(typeof(EventMcpTools), nameof(EventMcpTools.FireRoutedEvent))]
    public void MutationAndInteractionToolDescriptions_ShouldPreferVerboseAndCompactWording(Type toolType, string methodName)
    {
        var description = GetDescription(toolType, methodName);

        description.Should().Contain("detail");
        description.Should().Contain("compact");
        description.Should().Contain("verbose");
        description.Should().Contain("standard",
            "the legacy detail keyword should remain documented as a compatibility alias during the transition");
    }

    [Theory]
    [InlineData(typeof(MutationBatchMcpTools), nameof(MutationBatchMcpTools.BatchMutate))]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetActiveProcess))]
    [InlineData(typeof(BindingMcpTools), nameof(BindingMcpTools.GetAffectedElements))]
    public void AiFacingToolDescriptions_ShouldIncludeUseWhenAndDoNotUseGuidance(Type toolType, string methodName)
    {
        var description = GetDescription(toolType, methodName);

        description.Should().Contain("USE WHEN:",
            $"{toolType.Name}.{methodName} should include positive selection guidance for AI clients");
        description.Should().Contain("DO NOT USE:",
            $"{toolType.Name}.{methodName} should include negative selection guidance for AI clients");
    }

    [Fact]
    public void ConnectDescription_ShouldGuideSceneFirstFollowUps_AndDirectAutoDiscoveryOverrides()
    {
        var description = GetDescription(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect));

        description.Should().Contain("get_ui_summary");
        description.Should().Contain("get_element_snapshot");
        description.Should().Contain("get_form_summary");
        description.Should().Contain("connect(windowFilter='all')");
        description.Should().Contain("connect(selectionStrategy='largest_working_set', windowFilter='all')");
    }

    [Theory]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses))]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect))]
    [InlineData(typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindings))]
    [InlineData(typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingErrors))]
    [InlineData(typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetUiSummary))]
    [InlineData(typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetElementSnapshot))]
    [InlineData(typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetFormSummary))]
    public void HighValueToolDescriptions_ShouldReferenceMachineReadableContract_AndAvoidInlineSchemaBlocks(Type toolType, string methodName)
    {
        var description = GetDescription(toolType, methodName);

        description.Should().Contain("structuredContent");
        description.Should().Contain("content[0].text");
        description.Should().Contain("wpf://contracts/response");
        description.Should().NotContain("RESPONSE FORMAT:");
        description.Should().NotContain("ERRORS:");
    }

    [Theory]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses))]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect))]
    [InlineData(typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindings))]
    [InlineData(typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingErrors))]
    [InlineData(typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetUiSummary))]
    [InlineData(typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetElementSnapshot))]
    [InlineData(typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetFormSummary))]
    public void HighValueToolDescriptions_ShouldSeparateResponseFieldsFromRequestOptions(Type toolType, string methodName)
    {
        var description = GetDescription(toolType, methodName);

        description.Should().Contain("RESPONSE FIELDS:");
        description.Should().Contain("REQUEST OPTIONS:");
    }

    [Fact]
    public void GetFormSummaryDescription_ShouldDescribeNestedSummarySubmittabilityFields()
    {
        var description = GetDescription(typeof(SceneDiagnosticsMcpTools), nameof(SceneDiagnosticsMcpTools.GetFormSummary));

        description.Should().Contain("summary.validationSubmittable");
        description.Should().Contain("summary.interactionSubmittable");
        description.Should().Contain("summary.isSubmittable");
        description.Should().NotContain("RESPONSE FIELDS: formScope, inputs, commands, summary, validationSubmittable",
            "submittability fields are nested under summary rather than emitted at the top level");
    }

    private static string GetDescription(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var description = method!.GetCustomAttribute<DescriptionAttribute>();
        description.Should().NotBeNull();
        return description!.Description;
    }
}
