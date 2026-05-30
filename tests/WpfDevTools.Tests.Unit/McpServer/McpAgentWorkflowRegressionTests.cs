using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpPrompts;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpAgentWorkflowRegressionTests
{
    [Fact]
    public void ReadOnlyDiagnosisWorkflow_ShouldStartWithConnectAndSceneFirstInspection()
    {
        var workflow = ExtractToolCalls(WorkflowPrompts.StartDiagnostics());
        var policy = SensitiveReadPolicy();

        workflow.Should().StartWith("connect");
        workflow.Should().HaveElementAt(1, "get_ui_summary",
            "agent-facing read-only diagnosis should establish scene context before element or binding detail");
        workflow.Should().NotContain("get_visual_tree",
            "tree-heavy inspection should not be the default first diagnostic path");
        AssertWorkflowAllowed(policy, workflow);
    }

    [Fact]
    public void BindingErrorWorkflow_ShouldDiagnoseErrorsBeforeExpandingBindingDetails()
    {
        var workflow = ExtractToolCalls(WorkflowPrompts.DebugBindingIssue());

        workflow.Should().HaveElementAt(1, "get_binding_errors");
        workflow.Should().ContainInOrder("get_binding_errors", "get_bindings");
        AssertWorkflowAllowed(BindingDiagnosticPolicy(), workflow);
    }

    [Fact]
    public void MutationWorkflow_ShouldCaptureDiffAndRestoreInOrder()
    {
        var workflow = ExtractToolCalls(WorkflowPrompts.DebugCommandOrClick());

        workflow.Should().ContainInOrder("connect", "capture_state_snapshot", "click_element", "get_state_diff", "restore_state_snapshot");
        AssertWorkflowAllowed(FullMutationPolicy(), workflow);
    }

    [Fact]
    public void ScreenshotWorkflow_ShouldBeBlockedByDefaultAndAllowedOnlyByScreenshotGate()
    {
        var blocked = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "false",
            allowScreenshots: "false",
            allowViewModelInspection: "false",
            allowSensitiveReads: "true");

        blocked.EvaluateToolCall("element_screenshot").PolicyCategory.Should().Be("screenshots");

        var allowed = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "false",
            allowScreenshots: "true",
            allowViewModelInspection: "false",
            allowSensitiveReads: "true");

        allowed.EvaluateToolCall("element_screenshot").IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void ViewModelWorkflow_ShouldSeparateReadGateFromMutationGate()
    {
        var viewModelReadOnly = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "false",
            allowScreenshots: "false",
            allowViewModelInspection: "true",
            allowSensitiveReads: "true");

        viewModelReadOnly.EvaluateToolCall("get_viewmodel").IsAllowed.Should().BeTrue();
        viewModelReadOnly.EvaluateToolCall("modify_viewmodel").PolicyCategory.Should().Be("destructive-tools");

        FullMutationPolicy().EvaluateToolCall("modify_viewmodel").IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void BatchMutationWorkflow_ShouldDenyStringifiedNestedViewModelBypassWhenGateIsDisabled()
    {
        var policy = McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "false",
            allowViewModelInspection: "false",
            allowSensitiveReads: "true");
        using var document = JsonDocument.Parse(
            "{\"mutations\":\"[{\\\"tool\\\":\\\"modify_viewmodel\\\",\\\"args\\\":{\\\"propertyName\\\":\\\"Name\\\",\\\"value\\\":\\\"Alice\\\"}}]\"}");
        var arguments = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        var decision = policy.EvaluateToolCall("batch_mutate", arguments);

        decision.IsAllowed.Should().BeFalse();
        decision.PolicyCategory.Should().Be("viewmodel-inspection");
    }

    private static McpToolExecutionPolicy SensitiveReadPolicy() =>
        McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "false",
            allowScreenshots: "false",
            allowViewModelInspection: "false",
            allowSensitiveReads: "true");

    private static McpToolExecutionPolicy FullMutationPolicy() =>
        McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "true",
            allowScreenshots: "true",
            allowViewModelInspection: "true",
            allowSensitiveReads: "true");

    private static McpToolExecutionPolicy BindingDiagnosticPolicy() =>
        McpToolExecutionPolicy.FromConfiguredValues(
            allowDestructiveTools: "false",
            allowScreenshots: "false",
            allowViewModelInspection: "true",
            allowSensitiveReads: "true");

    private static void AssertWorkflowAllowed(
        McpToolExecutionPolicy policy,
        IEnumerable<string> workflow)
    {
        foreach (var toolName in workflow)
        {
            policy.EvaluateToolCall(toolName).IsAllowed.Should().BeTrue($"{toolName} should be allowed in this scripted workflow");
        }
    }

    private static IReadOnlyList<string> ExtractToolCalls(string agentFacingArtifact)
    {
        var toolNames = agentFacingArtifact
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => !line.Contains(McpServerConfiguration.AllowedTargetsEnvVar, StringComparison.Ordinal))
            .SelectMany(line => Regex.Matches(
                line,
                @"(?<![A-Za-z0-9_])([a-z][a-z0-9_]+)\s*\(",
                RegexOptions.CultureInvariant))
            .Select(match => match.Groups[1].Value)
            .ToArray();

        toolNames.Should().NotBeEmpty("workflow regression tests must read real agent-facing prompt artifacts");
        return toolNames;
    }
}
