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
    [InlineData(typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.FindBindingLeaks), "potentialLeaks")]
    [InlineData(typeof(TreeMcpTools), nameof(TreeMcpTools.GetWindows), "isMainWindow")]
    [InlineData(typeof(TreeMcpTools), nameof(TreeMcpTools.GetWindows), "index, title, type, isActive, isVisible, isMainWindow, elementId")]
    [InlineData(typeof(TreeMcpTools), nameof(TreeMcpTools.GetNamescope), "inactive tabs")]
    [InlineData(typeof(EventMcpTools), nameof(EventMcpTools.GetEventHandlers), "mayBeIncomplete")]
    [InlineData(typeof(InteractionMcpTools), nameof(InteractionMcpTools.SimulateKeyboard), "focusChanged")]
    [InlineData(typeof(StateMcpTools), nameof(StateMcpTools.RestoreStateSnapshot), "skippedViewModelPropertyCount")]
    [InlineData(typeof(StateMcpTools), nameof(StateMcpTools.RestoreStateSnapshot), "skippedViewModelProperties")]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.GetViewModel), "canWrite")]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.ModifyViewModel), "requestedValueType")]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.GetValidationErrors), "logical and visual descendants")]
    [InlineData(typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingErrors), "validation rule errors")]
    [InlineData(typeof(StyleMcpTools), nameof(StyleMcpTools.GetAppliedStyles), "localResourceReferences")]
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
    public void RuntimeNavigationToolDescriptions_ShouldMentionReturnedNextStepsGuidance()
    {
        foreach (var (toolType, methodName) in RuntimeNavigationTools)
        {
            var description = GetDescription(toolType, methodName);

            description.Should().Contain("nextSteps",
                $"{toolType.Name}.{methodName} should document runtime navigation guidance");
            description.Should().Contain("navigation.recommended",
                $"{toolType.Name}.{methodName} should prefer the richer navigation envelope");
            description.Should().Contain("compatibility field",
                $"{toolType.Name}.{methodName} should explain that nextSteps is retained for older clients");
            description.Should().Contain("ad hoc tool guessing",
                $"{toolType.Name}.{methodName} should direct agents to prefer returned guidance");
        }
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

    private static string GetDescription(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var description = method!.GetCustomAttribute<DescriptionAttribute>();
        description.Should().NotBeNull();
        return description!.Description;
    }
}
