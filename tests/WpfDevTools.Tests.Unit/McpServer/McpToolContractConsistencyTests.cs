using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
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
    public void ForceBindingUpdate_ShouldExposeOptionalDirection()
    {
        var parameter = GetParameter(typeof(BindingMcpTools), nameof(BindingMcpTools.ForceBindingUpdate), "direction");

        parameter.ParameterType.Should().Be(typeof(string));
        parameter.HasDefaultValue.Should().BeTrue();
        parameter.DefaultValue.Should().BeNull();
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
    [InlineData(typeof(MvvmMcpTools), nameof(MvvmMcpTools.ModifyViewModel))]
    [InlineData(typeof(StyleMcpTools), nameof(StyleMcpTools.OverrideStyleSetter))]
    public void ValueMutationTools_ShouldAcceptJsonValue(Type toolType, string methodName)
    {
        var parameter = GetParameter(toolType, methodName, "value");

        parameter.ParameterType.Should().Be(typeof(JsonElement));
        parameter.HasDefaultValue.Should().BeFalse();
    }

    private static ParameterInfo GetParameter(Type declaringType, string methodName, string parameterName)
    {
        var method = declaringType.GetMethod(methodName);
        method.Should().NotBeNull();

        var parameter = method!.GetParameters().SingleOrDefault(item => item.Name == parameterName);
        parameter.Should().NotBeNull($"{declaringType.Name}.{methodName} should expose '{parameterName}'");
        return parameter!;
    }
}
