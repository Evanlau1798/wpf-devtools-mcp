using System.Reflection;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class SceneDiagnosticsContractTests
{
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
}
