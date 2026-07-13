using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public class McpToolAnnotationSemanticsTests
{
    [Fact]
    public void ApplyUiBlueprint_ShouldAdvertiseDestructiveCapableProjectWrites()
    {
        var method = typeof(UiComposerMcpTools).GetMethod(nameof(UiComposerMcpTools.ApplyUiBlueprint));
        method.Should().NotBeNull();

        var attribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        attribute.Should().NotBeNull();
        attribute!.ReadOnly.Should().BeFalse(
            "apply_ui_blueprint can persist generated XAML when dryRun is false");
        attribute.Destructive.Should().BeTrue(
            "apply_ui_blueprint is destructive-capable even though dryRun defaults to true");
    }

    [Fact]
    public void ApplyUiProjectIntegration_ShouldAdvertiseDestructiveProjectWrites()
    {
        var method = typeof(UiComposerMcpTools).GetMethod(nameof(UiComposerMcpTools.ApplyUiProjectIntegration));
        method.Should().NotBeNull();

        var attribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        attribute.Should().NotBeNull();
        attribute!.ReadOnly.Should().BeFalse();
        attribute.Destructive.Should().BeTrue();
    }

    [Fact]
    public void WatchDpChanges_ShouldAdvertiseStatefulNonDestructiveRegistration()
    {
        var method = typeof(DependencyPropertyMcpTools).GetMethod(nameof(DependencyPropertyMcpTools.WatchDpChanges));
        method.Should().NotBeNull();

        var attribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        attribute.Should().NotBeNull();
        attribute!.ReadOnly.Should().BeFalse(
            "watch_dp_changes registers transient watcher state and is not a pure read");
        attribute.Destructive.Should().BeFalse(
            "watch_dp_changes does not perform destructive updates to the inspected application");
    }

    [Theory]
    [InlineData(typeof(EventMcpTools), nameof(EventMcpTools.TraceRoutedEvents), "trace_routed_events")]
    public void StatefulObserverTools_ShouldAdvertiseNonReadOnlyNonDestructiveSemantics(
        Type toolType,
        string methodName,
        string toolName)
    {
        var method = toolType.GetMethod(methodName);
        method.Should().NotBeNull();

        var attribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        attribute.Should().NotBeNull();
        attribute!.ReadOnly.Should().BeFalse(
            $"{toolName} mutates session-scoped observer or snapshot state and is not a pure read");
        attribute.Destructive.Should().BeFalse(
            $"{toolName} records or registers session-scoped observer state without consuming or replacing existing state");
    }

    [Theory]
    [InlineData(typeof(EventDrainMcpTools), nameof(EventDrainMcpTools.DrainEvents), "drain_events")]
    [InlineData(typeof(StateMcpTools), nameof(StateMcpTools.CaptureStateSnapshot), "capture_state_snapshot")]
    public void StateConsumingObserverTools_ShouldAdvertiseDestructiveSemantics(
        Type toolType,
        string methodName,
        string toolName)
    {
        var method = toolType.GetMethod(methodName);
        method.Should().NotBeNull();

        var attribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        attribute.Should().NotBeNull();
        attribute!.ReadOnly.Should().BeFalse(
            $"{toolName} mutates session-scoped observer or snapshot state and is not a pure read");
        attribute.Destructive.Should().BeTrue(
            $"{toolName} consumes or replaces existing session-scoped state, so it is not additive-only under MCP annotations");
    }
}
