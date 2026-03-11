using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolContractDescriptionTests
{
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
    public void ToolDescriptions_ShouldMentionUpdatedContractTerms(Type toolType, string methodName, string expectedTerm)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var description = method!.GetCustomAttribute<DescriptionAttribute>();
        description.Should().NotBeNull();
        description!.Description.Should().Contain(expectedTerm);
    }
}
