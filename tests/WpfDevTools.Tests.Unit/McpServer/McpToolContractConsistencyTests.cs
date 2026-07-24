using System.Reflection;
using System.Text.Json;
using System.ComponentModel;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Schema;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public class McpToolContractConsistencyTests
{
    [Fact]
    public void DragAndDrop_ShouldExposeOptionalDataFormat()
    {
        var parameter = GetParameter(typeof(InteractionMcpTools), nameof(InteractionMcpTools.DragAndDrop), "dataFormat");

        parameter.ParameterType.Should().Be(typeof(string));
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void SimulateKeyboard_ShouldExposeOptionalEventType()
    {
        var parameter = GetParameter(typeof(InteractionMcpTools), nameof(InteractionMcpTools.SimulateKeyboard), "eventType");

        parameter.ParameterType.Should().Be(typeof(string));
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void ElementScreenshot_ShouldNotExposeOutputPath()
    {
        var method = typeof(InteractionMcpTools).GetMethod(nameof(InteractionMcpTools.ElementScreenshot));

        method.Should().NotBeNull();
        method!.GetParameters().Select(parameter => parameter.Name).Should().NotContain("outputPath");
        method.GetCustomAttribute<DescriptionAttribute>()!.Description.Should().Contain("resourceRead.chunking");
    }

    [Fact]
    public void CompareTrees_ShouldExposeOptionalElementId()
    {
        var parameter = GetParameter(typeof(TreeMcpTools), nameof(TreeMcpTools.CompareTrees), "elementId");

        parameter.ParameterType.Should().Be(typeof(string));
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void GetVisualTree_ShouldExposeOptionalCompressionParameters()
    {
        AssertOptionalParameter(typeof(TreeMcpTools), nameof(TreeMcpTools.GetVisualTree), "compact", typeof(bool), false);
        AssertOptionalParameter(typeof(TreeMcpTools), nameof(TreeMcpTools.GetVisualTree), "summaryOnly", typeof(bool), false);
        AssertOptionalParameter(typeof(TreeMcpTools), nameof(TreeMcpTools.GetVisualTree), "maxNodes", typeof(int?), null);
        AssertOptionalParameter(typeof(TreeMcpTools), nameof(TreeMcpTools.GetVisualTree), "maxChildrenPerNode", typeof(int?), null);
    }

    [Fact]
    public void GetLogicalTree_ShouldExposeOptionalCompressionParameters()
    {
        AssertOptionalParameter(typeof(TreeMcpTools), nameof(TreeMcpTools.GetLogicalTree), "compact", typeof(bool), false);
        AssertOptionalParameter(typeof(TreeMcpTools), nameof(TreeMcpTools.GetLogicalTree), "summaryOnly", typeof(bool), false);
        AssertOptionalParameter(typeof(TreeMcpTools), nameof(TreeMcpTools.GetLogicalTree), "maxNodes", typeof(int?), null);
        AssertOptionalParameter(typeof(TreeMcpTools), nameof(TreeMcpTools.GetLogicalTree), "maxChildrenPerNode", typeof(int?), null);
    }

    [Fact]
    public void GetTemplateTree_ShouldExposeOptionalPayloadCaps()
    {
        AssertOptionalParameter(typeof(TreeMcpTools), nameof(TreeMcpTools.GetTemplateTree), "maxNodes", typeof(int?), null);
        AssertOptionalParameter(typeof(TreeMcpTools), nameof(TreeMcpTools.GetTemplateTree), "maxChildrenPerNode", typeof(int?), null);
    }

    [Theory]
    [InlineData(nameof(TreeMcpTools.GetVisualTree))]
    [InlineData(nameof(TreeMcpTools.GetLogicalTree))]
    public void TreeTools_ShouldDocumentCompressedResponseShapes(string methodName)
    {
        var method = typeof(TreeMcpTools).GetMethod(methodName);

        method.Should().NotBeNull();
        var description = method!.GetCustomAttribute<DescriptionAttribute>();

        description.Should().NotBeNull();
        description!.Description.Should().Contain("flat-summary-v1");
        description.Description.Should().Contain("returnedNodeCount");
        description.Description.Should().Contain("omittedNodeCount");
        description.Description.Should().Contain("truncated");
        description.Description.Should().Contain("appliedOptions");
        description.Description.Should().Contain("depthSufficiencyHint");
    }

    [Fact]
    public void ForceBindingUpdate_ShouldExposeOptionalDirection()
    {
        var parameter = GetParameter(typeof(BindingMcpTools), nameof(BindingMcpTools.ForceBindingUpdate), "direction");

        parameter.ParameterType.Should().Be(typeof(string));
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void ToolNextStep_ShouldExposeOptionalConditionalNavigationFields()
    {
        typeof(ToolNextStep).GetProperty(nameof(ToolNextStep.Preconditions))!.PropertyType.Should().Be(typeof(IReadOnlyList<string>));
        typeof(ToolNextStep).GetProperty(nameof(ToolNextStep.ExpectedOutcome))!.PropertyType.Should().Be(typeof(string));
        typeof(ToolNextStep).GetProperty(nameof(ToolNextStep.WorkflowId))!.PropertyType.Should().Be(typeof(string));
        typeof(ToolNextStep).GetProperty(nameof(ToolNextStep.PrefetchTools))!.PropertyType.Should().Be(typeof(IReadOnlyList<string>));
        typeof(ToolNextStep).GetProperty(nameof(ToolNextStep.WhyNow))!.PropertyType.Should().Be(typeof(string));
        typeof(ToolNextStep).GetProperty(nameof(ToolNextStep.Confidence))!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void ToolNavigationEnvelope_ShouldExposeStableCollectionShapes()
    {
        typeof(ToolNavigationEnvelope).GetProperty(nameof(ToolNavigationEnvelope.Recommended))!.PropertyType.Should().Be(typeof(IReadOnlyList<ToolNextStep>));
        typeof(ToolNavigationEnvelope).GetProperty(nameof(ToolNavigationEnvelope.Alternatives))!.PropertyType.Should().Be(typeof(IReadOnlyList<ToolNextStep>));
        typeof(ToolNavigationEnvelope).GetProperty(nameof(ToolNavigationEnvelope.PrefetchTools))!.PropertyType.Should().Be(typeof(IReadOnlyList<string>));
        typeof(ToolNavigationEnvelope).GetProperty(nameof(ToolNavigationEnvelope.ContextRefs))!.PropertyType.Should().Be(typeof(IReadOnlyList<ToolNavigationReference>));
    }

    [Fact]
    public void ToolNavigationReference_ShouldExposeType()
    {
        typeof(ToolNavigationReference).GetProperty(nameof(ToolNavigationReference.Type))!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void TraceRoutedEvents_ShouldExposeOptionalMode()
    {
        var parameter = GetParameter(typeof(EventMcpTools), nameof(EventMcpTools.TraceRoutedEvents), "mode");

        parameter.ParameterType.Should().Be(typeof(string));
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void FindBindingLeaks_ShouldExposeOptionalSamplingDurationMs()
    {
        var parameter = GetParameter(typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.FindBindingLeaks), "samplingDurationMs");

        parameter.ParameterType.Should().Be(typeof(int?));
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void GetRenderStats_ShouldExposeOptionalWarmUp()
    {
        var parameter = GetParameter(typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.GetRenderStats), "warmUp");

        parameter.ParameterType.Should().Be(typeof(bool));
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().Be(false);
    }

    [Fact]
    public void FindBindingLeaks_ShouldExposeOptionalWarmUp()
    {
        var parameter = GetParameter(typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.FindBindingLeaks), "warmUp");

        parameter.ParameterType.Should().Be(typeof(bool));
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().Be(false);
    }

    [Fact]
    public void FireRoutedEvent_ShouldExposeOptionalEventArgs()
    {
        var parameter = GetParameter(typeof(EventMcpTools), nameof(EventMcpTools.FireRoutedEvent), "eventArgs");

        parameter.ParameterType.Should().Be(typeof(JsonElement?));
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().BeNull();
    }

    [Theory]
    [InlineData(typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.SetDpValue))]
    [InlineData(typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.ClearDpValue))]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.ModifyViewModel))]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.ExecuteCommand))]
    [InlineData(typeof(StyleMcpTools), nameof(StyleMcpTools.OverrideStyleSetter))]
    [InlineData(typeof(InteractionMcpTools), nameof(InteractionMcpTools.ClickElement))]
    [InlineData(typeof(EventMcpTools), nameof(EventMcpTools.FireRoutedEvent))]
    public void MutationTools_ShouldExposeOptionalDetailMode(Type toolType, string methodName)
    {
        AssertOptionalParameter(toolType, methodName, "detail", typeof(string), null);
    }

    [Theory]
    [InlineData(typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.SetDpValue))]
    public void ValueMutationTools_ShouldAcceptJsonValue(Type toolType, string methodName)
    {
        var parameter = GetParameter(toolType, methodName, "value");

        parameter.ParameterType.Should().Be(typeof(JsonElement));
        parameter.HasDefaultValue.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindings))]
    [InlineData(typeof(LayoutMcpTools), nameof(LayoutMcpTools.GetLayoutInfo))]
    [InlineData(typeof(LayoutMcpTools), nameof(LayoutMcpTools.GetClippingInfo))]
    [InlineData(typeof(StyleMcpTools), nameof(StyleMcpTools.GetAppliedStyles))]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.GetValidationErrors))]
    public void ElementBatchTools_ShouldExposeOptionalElementIds(Type toolType, string methodName)
    {
        AssertOptionalParameter(toolType, methodName, "elementIds", typeof(string[]), null);
    }

    [Fact]
    public void GetDpValueSource_ShouldExposeOptionalBatchParameters()
    {
        AssertOptionalParameter(typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.GetDpValueSource), "propertyName", typeof(string), null);
        AssertOptionalParameter(typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.GetDpValueSource), "elementIds", typeof(string[]), null);
        AssertOptionalParameter(typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.GetDpValueSource), "propertyNames", typeof(string[]), null);
    }

    [Theory]
    [InlineData(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Ping))]
    [InlineData(typeof(TreeMcpTools), nameof(TreeMcpTools.GetVisualTree))]
    [InlineData(typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindings))]
    [InlineData(typeof(DependencyPropertyMcpTools), nameof(DependencyPropertyMcpTools.GetDpValueSource))]
    [InlineData(typeof(StyleMcpTools), nameof(StyleMcpTools.GetAppliedStyles))]
    [InlineData(typeof(EventMcpTools), nameof(EventMcpTools.GetEventHandlers))]
    [InlineData(typeof(InteractionMcpTools), nameof(InteractionMcpTools.ClickElement))]
    [InlineData(typeof(LayoutMcpTools), nameof(LayoutMcpTools.GetLayoutInfo))]
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.GetViewModel))]
    [InlineData(typeof(PerformanceMcpTools), nameof(PerformanceMcpTools.GetRenderStats))]
    [InlineData(typeof(StateMcpTools), nameof(StateMcpTools.CaptureStateSnapshot))]
    public void ConnectedTools_ShouldExposeOptionalProcessId_ForActiveProcessWorkflows(Type toolType, string methodName)
    {
        AssertOptionalParameter(toolType, methodName, "processId", typeof(int?), null);
    }

    [Fact]
    public void Connect_ShouldExposeOptionalProcessId_AndSelectionStrategy()
    {
        AssertOptionalParameter(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "processId", typeof(int?), null);
        AssertOptionalParameter(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "selectionStrategy", typeof(string), null);
        AssertOptionalParameter(typeof(ProcessMcpTools), nameof(ProcessMcpTools.Connect), "windowFilter", typeof(string), null);
    }

    [Fact]
    public void GetProcesses_ShouldExposeOptionalWindowFilter()
    {
        AssertOptionalParameter(typeof(ProcessMcpTools), nameof(ProcessMcpTools.GetProcesses), "windowFilter", typeof(string), null);
    }

    [Fact]
    public void FindElements_ShouldExposeOptionalTypeMatchMode()
    {
        AssertOptionalParameter(typeof(TreeMcpTools), nameof(TreeMcpTools.FindElements), "typeMatchMode", typeof(string), null);
    }

    [Fact]
    public void PreviewUiBlueprint_ShouldExposeBoundedDiagnosticOptions()
    {
        AssertOptionalParameter(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.PreviewUiBlueprint), "screenshotOutputMode", typeof(string), "metadata");
        AssertOptionalParameter(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.PreviewUiBlueprint), "screenshotMaxWidth", typeof(int?), 1024);
        AssertOptionalParameter(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.PreviewUiBlueprint), "screenshotMaxHeight", typeof(int?), 1024);
        AssertOptionalParameter(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.PreviewUiBlueprint), "viewportWidth", typeof(int?), null);
        AssertOptionalParameter(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.PreviewUiBlueprint), "viewportHeight", typeof(int?), null);
        AssertOptionalParameter(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.PreviewUiBlueprint), "runtimePackApprovalTokens", typeof(string[]), null);
        AssertOptionalParameter(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.PreviewUiBlueprint), "correlationLookupLimit", typeof(int), 32);
        var lookupLimit = GetParameter(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.PreviewUiBlueprint), "correlationLookupLimit");
        lookupLimit.GetCustomAttribute<DescriptionAttribute>()!.Description.Should()
            .Contain("non-generated correlation names (authored elementName values and renderer-provided root x:Name values)");
    }

    [Fact]
    public void ApplyUiBlueprint_ShouldExposeOptionalTargetWindowSize()
    {
        AssertOptionalParameter(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.ApplyUiBlueprint), "targetWindowWidth", typeof(int?), null);
        AssertOptionalParameter(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.ApplyUiBlueprint), "targetWindowHeight", typeof(int?), null);
    }

    [Fact]
    public void GetUiBlockCatalog_ShouldExposeBoundedAllowedValueSearch()
    {
        AssertOptionalParameter(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.GetUiBlockCatalog), "allowedValueQuery", typeof(string), null);
    }

    [Fact]
    public void SelectActiveProcess_ShouldExposeOptionalNullableProcessId_ForStructuredMissingParameter()
    {
        AssertOptionalParameter(typeof(ProcessMcpTools), nameof(ProcessMcpTools.SelectActiveProcess), "processId", typeof(int?), null);
    }

    [Fact]
    public void AllConnectedMcpTools_ShouldExposeOptionalProcessId_ExceptConnectionSelectionEntrypoints()
    {
        var exemptToolNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "connect",
            "select_active_process"
        };

        var toolMethods = typeof(ProcessMcpTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => new
            {
                Method = method,
                Tool = method.GetCustomAttribute<McpServerToolAttribute>()
            })
            .Where(item => item.Tool != null)
            .Where(item => item.Tool!.Name != null && !exemptToolNames.Contains(item.Tool.Name))
            .Select(item => item.Method);

        foreach (var method in toolMethods)
        {
            var processId = method.GetParameters().SingleOrDefault(parameter => parameter.Name == "processId");
            if (processId == null)
            {
                continue;
            }

            processId.ParameterType.Should().Be(typeof(int?),
                $"{method.DeclaringType!.Name}.{method.Name} should allow processId omission after active-process selection");
            processId.HasDefaultValue.Should().BeTrue();
            processId.DefaultValue.Should().BeNull();
        }
    }

    private static ParameterInfo GetParameter(Type declaringType, string methodName, string parameterName)
    {
        var method = declaringType.GetMethod(methodName);
        method.Should().NotBeNull();

        var parameter = method!.GetParameters().SingleOrDefault(item => item.Name == parameterName);
        parameter.Should().NotBeNull($"{declaringType.Name}.{methodName} should expose '{parameterName}'");
        return parameter!;
    }

    private static void AssertOptionalParameter(
        Type declaringType,
        string methodName,
        string parameterName,
        Type parameterType,
        object? defaultValue)
    {
        var parameter = GetParameter(declaringType, methodName, parameterName);
        parameter.ParameterType.Should().Be(parameterType);
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().Be(defaultValue);
    }
}

