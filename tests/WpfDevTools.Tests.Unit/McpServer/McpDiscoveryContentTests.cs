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
    }

    [Fact]
    public void WorkflowPrompts_ShouldCoverBindingCommandAndSecondaryWindowScenarios()
    {
        WorkflowPrompts.DebugBindingIssue().Should().Contain("get_binding_errors");
        WorkflowPrompts.DebugBindingIssue().Should().Contain("get_element_snapshot");
        WorkflowPrompts.DebugCommandOrClick().Should().Contain("click_element");
        WorkflowPrompts.DebugCommandOrClick().Should().Contain("get_interaction_readiness");
        WorkflowPrompts.ConnectAndListWindows().Should().Contain("get_windows");
        WorkflowPrompts.ConnectAndListWindows().Should().Contain("connect()");
        WorkflowPrompts.ConnectAndListWindows().Should().Contain("windowFilter");
        WorkflowPrompts.InspectSecondaryWindow().Should().Contain("Application.MainWindow");
    }
}
