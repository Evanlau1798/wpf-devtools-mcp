using System.Reflection;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpPrompts;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public class McpDiscoveryContentTests
{
    [Fact]
    public void CapabilitiesResource_ShouldExposeVersionTransportAndWorkflowHints()
    {
        var content = CapabilityResources.GetCapabilities();

        content.Should().Contain("wpf-devtools-mcp");
        content.Should().Contain(ServerMetadata.GetDisplayVersion());
        content.Should().Contain(ServerMetadata.GetSchemaVersion());
        content.Should().Contain("stdio");
        content.Should().Contain("slash commands");
        content.Should().Contain("@resource");
        content.Should().Contain("stateSnapshots");
        content.Should().Contain("performance profiling");
        content.Should().Contain("runtime state safety notes");
    }

    [Fact]
    public void LimitationResources_ShouldDocumentElevatedTargetsWindowFocusAndStateSafety()
    {
        CapabilityResources.GetElevatedTargetLimitations().Should().Contain("administrator");
        CapabilityResources.GetElevatedTargetLimitations().Should().Contain("stdio");
        CapabilityResources.GetInjectionFailureLimitations().Should().Contain("architecture mismatch");
        CapabilityResources.GetInjectionFailureLimitations().Should().Contain("SDK mode");

        CapabilityResources.GetWindowFocusLimitations().Should().Contain("Application.MainWindow");
        CapabilityResources.GetWindowFocusLimitations().Should().Contain("get_windows");

        CapabilityResources.GetStateSafetyNotes().Should().Contain("Snapshot/restore");
        CapabilityResources.GetStateSafetyNotes().Should().Contain("capture_state_snapshot");
        CapabilityResources.GetStateSafetyNotes().Should().Contain("Binding-backed DependencyProperties captured in the same session can be restored");
        CapabilityResources.GetStateSafetyNotes().Should().Contain("non-Binding expressions are still surfaced as skipped capability boundaries");
        typeof(CapabilityResources)
            .GetMethod(nameof(CapabilityResources.GetStateSafetyNotes))!
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .Cast<System.ComponentModel.DescriptionAttribute>()
            .Single()
            .Description
            .Should().NotContain("until snapshot/restore helpers exist",
                "resource descriptions should not advertise outdated pre-snapshot wording");
    }

    [Fact]
    public void WorkflowPrompts_ShouldCoverBindingCommandAndSecondaryWindowScenarios()
    {
        WorkflowPrompts.DebugBindingIssue().Should().Contain("get_binding_errors");
        WorkflowPrompts.DebugBindingIssue().Should().Contain("get_element_snapshot");
        WorkflowPrompts.DebugBindingIssue().Should().Contain("navigation.recommended");
        WorkflowPrompts.DebugCommandOrClick().Should().Contain("click_element");
        WorkflowPrompts.DebugCommandOrClick().Should().Contain("get_interaction_readiness");
        WorkflowPrompts.DebugCommandOrClick().Should().Contain("trace_routed_events(mode='get')");
        WorkflowPrompts.ProfilePerformance().Should().Contain("get_render_stats");
        WorkflowPrompts.ProfilePerformance().Should().Contain("measure_element_render_time");
        WorkflowPrompts.ConnectAndListWindows().Should().Contain("get_windows");
        WorkflowPrompts.ConnectAndListWindows().Should().Contain("connect()");
        WorkflowPrompts.ConnectAndListWindows().Should().Contain("windowFilter");
        WorkflowPrompts.ConnectAndListWindows().Should().Contain("Do not call get_processes");
        WorkflowPrompts.InspectSecondaryWindow().Should().Contain("Application.MainWindow");
        WorkflowPrompts.InspectSecondaryWindow().Should().Contain("connect()");
    }

    [Fact]
    public void CapabilityResources_ShouldPreferConnectFirstAndSceneFirstGuidance()
    {
        var capabilities = CapabilityResources.GetCapabilities();
        var bindingWorkflow = CapabilityResources.GetBindingWorkflow();

        capabilities.Should().Contain("connect()");
        capabilities.Should().Contain("get_ui_summary");
        capabilities.Should().Contain("navigation.recommended");
        capabilities.Should().Contain("compatibility `nextSteps`");
        capabilities.Should().NotContain("nextSteps / `navigation` guidance",
            "capability guidance should explicitly prefer navigation.recommended over the compatibility field");
        bindingWorkflow.Should().Contain("navigation.recommended");
        bindingWorkflow.Should().Contain("get_element_snapshot");
        bindingWorkflow.Should().NotContain("get_visual_tree or get_logical_tree",
            "binding workflow should not recommend tree expansion before scene-level diagnostics");
    }

    [Fact]
    public void CapabilityResources_ShouldKeepSnapshotSummaryAlignedWithStateSafetyNotes()
    {
        var capabilities = CapabilityResources.GetCapabilities();
        var stateSafety = CapabilityResources.GetStateSafetyNotes();

        capabilities.Should().Contain("Binding-backed DependencyProperties captured in the same session",
            "the summary resource should not understate rollback support compared with the detailed state-safety note");
        stateSafety.Should().Contain("Binding-backed DependencyProperties captured in the same session");
    }
}
