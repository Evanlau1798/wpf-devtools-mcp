using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Tests for ServerInstructions - validates the server instructions content
/// that is sent to MCP clients during initialization.
/// </summary>
public class ServerInstructionsTests
{
    [Fact]
    public void Value_ShouldNotBeNullOrEmpty()
    {
        ServerInstructions.Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Value_ShouldContainMandatoryWorkflow()
    {
        ServerInstructions.Value.Should().Contain("MANDATORY WORKFLOW");
        ServerInstructions.Value.Should().Contain("connect()");
        ServerInstructions.Value.Should().Contain("auto-discovery");
        ServerInstructions.Value.Should().Contain("windowFilter");
        ServerInstructions.Value.Should().Contain("Do not call get_processes before connect()");
        ServerInstructions.Value.Should().Contain("Prefer get_ui_summary");
    }

    [Fact]
    public void Value_ShouldContainParameterConventions()
    {
        ServerInstructions.Value.Should().Contain("PARAMETER CONVENTIONS");
        ServerInstructions.Value.Should().Contain("processId");
        ServerInstructions.Value.Should().Contain("elementId");
    }

    [Fact]
    public void Value_ShouldSeparateSceneAndTreeDepthDefaults()
    {
        ServerInstructions.Value.Should().Contain("depth: integer (0-100)");
        ServerInstructions.Value.Should().Contain("Tree tools default to depth=10 when depth is omitted");
        ServerInstructions.Value.Should().Contain("get_ui_summary defaults to depth=3 and depthMode='semantic'");
        ServerInstructions.Value.Should().NotContain("depth: integer (1-100), controls tree traversal depth, default=10");
    }

    [Fact]
    public void Value_ShouldContainTimeouts()
    {
        ServerInstructions.Value.Should().Contain("TIMEOUTS");
        ServerInstructions.Value.Should().Contain("30 seconds");
        ServerInstructions.Value.Should().Contain("5 seconds");
    }

    [Fact]
    public void Value_ShouldContainRateLimits()
    {
        ServerInstructions.Value.Should().Contain("RATE LIMITS");
        ServerInstructions.Value.Should().Contain($"{McpServerConfiguration.RateLimitRequestsPerMinute} requests/minute");
    }

    [Fact]
    public void Value_ShouldContainToolSelectionGuide()
    {
        ServerInstructions.Value.Should().Contain("TOOL SELECTION GUIDE");
    }

    [Fact]
    public void Value_ShouldContainCommonWorkflows()
    {
        ServerInstructions.Value.Should().Contain("COMMON WORKFLOWS");
        ServerInstructions.Value.Should().Contain("Debug Binding Error");
        ServerInstructions.Value.Should().Contain("get_ui_summary");
        ServerInstructions.Value.Should().Contain("summaryOnly=true");
        ServerInstructions.Value.Should().Contain("get_element_snapshot");
        ServerInstructions.Value.Should().Contain("get_form_summary");
        ServerInstructions.Value.Should().Contain("get_state_diff");
        ServerInstructions.Value.Should().Contain("follow navigation.recommended");
    }

    [Fact]
    public void Value_ShouldGuideSceneFirstContextAndDirectConnectOverrides()
    {
        ServerInstructions.Value.Should().Contain("get_ui_summary or get_form_summary");
        ServerInstructions.Value.Should().Contain("get_element_snapshot(elementId) only after a concrete elementId is known",
            "server instructions should explicitly tie successful connect flows to scene-first context tools without implying element snapshots work before element discovery");
        ServerInstructions.Value.Should().Contain("connect(windowFilter='all')",
            "server instructions should show the direct hidden/background auto-discovery override");
        ServerInstructions.Value.Should().Contain("connect(selectionStrategy='largest_working_set', windowFilter='all')",
            "server instructions should show the explicit multi-process auto-selection override for advanced disambiguation");
    }

    [Fact]
    public void Value_ShouldDescribeSnapshotPrerequisite_BeforeGetStateDiffWorkflow()
    {
        ServerInstructions.Value.Should().Contain("capture_state_snapshot",
            "workflow guidance should not tell agents to call get_state_diff without first creating a snapshot");
    }

    [Fact]
    public void Value_ShouldKeepViewModelWorkflowElementScoped()
    {
        ServerInstructions.Value.Should().Contain("get_viewmodel(elementId)",
            "the view-model workflow should keep using the same element scope that was introduced earlier in the example");
        ServerInstructions.Value.Should().Contain("get_commands(elementId)",
            "the command inspection workflow should stay element-scoped to avoid silently switching to the root window DataContext");
    }

    [Fact]
    public void Value_ShouldContainErrorRecovery()
    {
        ServerInstructions.Value.Should().Contain("ERROR RECOVERY");
        ServerInstructions.Value.Should().Contain("not connected");
    }

    [Fact]
    public void Value_ShouldContainResponseFormat()
    {
        ServerInstructions.Value.Should().Contain("RESPONSE FORMAT");
        ServerInstructions.Value.Should().Contain("success");
    }

    [Fact]
    public void Value_ShouldDescribeStructuredErrorMetadata()
    {
        ServerInstructions.Value.Should().Contain("errorCode");
        ServerInstructions.Value.Should().Contain("errorData");
        ServerInstructions.Value.Should().Contain("recovery");
        ServerInstructions.Value.Should().Contain("parameterVocabularies");
    }

    [Fact]
    public void Value_ShouldExplainHowToUseRuntimeNavigation()
    {
        ServerInstructions.Value.Should().Contain("preferred follow-up surface");
        ServerInstructions.Value.Should().Contain("By default, tool responses include the additive `navigation` envelope");
        ServerInstructions.Value.Should().Contain("By default, tool responses also include compatibility `nextSteps`");
        ServerInstructions.Value.Should().Contain("nextSteps: []");
        ServerInstructions.Value.Should().Contain("ad hoc tool guessing");
        ServerInstructions.Value.Should().Contain("session-aware");
        ServerInstructions.Value.Should().Contain("workflowId");
        ServerInstructions.Value.Should().Contain("prefetchTools");
        ServerInstructions.Value.Should().Contain("advisory");
        ServerInstructions.Value.Should().Contain("navigation.recommended");
        ServerInstructions.Value.Should().Contain("compatibility field");
        ServerInstructions.Value.Should().Contain("descriptive JSON");
        ServerInstructions.Value.Should().Contain("already know the next step");
        ServerInstructions.Value.Should().Contain("get_binding_errors accepts navigation=false");
        ServerInstructions.Value.Should().Contain("Schema-driven clients can rely on that opt-out there");
        ServerInstructions.Value.Should().NotContain("Every tool response includes the additive `navigation` envelope",
            "navigation and nextSteps are default follow-up surfaces rather than unconditional fields on explicit opt-out calls");
        ServerInstructions.Value.Should().NotContain("use it as the preferred follow-up navigation field",
            "nextSteps should remain a compatibility field rather than the preferred navigation surface");
    }

    [Fact]
    public void Value_ShouldContainLimitations()
    {
        ServerInstructions.Value.Should().Contain("LIMITATIONS");
    }

    [Fact]
    public void Value_ShouldGuideToolSearchDrivenClients()
    {
        ServerInstructions.Value.Should().Contain("TOOL SEARCH");
        ServerInstructions.Value.Should().Contain("Title");
        ServerInstructions.Value.Should().Contain("structured");
    }

    [Fact]
    public void Value_ShouldApplyAdvancedToolUseGuidanceForLargeToolsets()
    {
        ServerInstructions.Value.Should().Contain("defer loading specialized tools until the task scope is known");
        ServerInstructions.Value.Should().Contain("run independent read-only inspections in parallel when the client supports it");
        ServerInstructions.Value.Should().Contain("summarize intermediate tool results before choosing the next call");
        ServerInstructions.Value.Should().Contain("complex parameters require concrete examples");
    }

    [Fact]
    public void Value_ShouldExplainElevatedTargetLimitations()
    {
        ServerInstructions.Value.Should().Contain("elevated");
        ServerInstructions.Value.Should().Contain("administrator");
    }

    [Fact]
    public void Value_ShouldExplainPromptAndResourceDiscovery()
    {
        ServerInstructions.Value.Should().Contain("slash commands");
        ServerInstructions.Value.Should().Contain("@resource");
        ServerInstructions.Value.Should().Contain("portable discovery contract");
        ServerInstructions.Value.Should().Contain("prompt names");
        ServerInstructions.Value.Should().Contain("resource URIs");
    }

    [Fact]
    public void Value_ShouldContainAllToolCategories()
    {
        // Check for key tool category concepts (case-insensitive, flexible matching)
        var categoryKeywords = new[] {
            ("Process", "process"),
            ("Tree", "Tree"),
            ("Binding", "Binding"),
            ("DependencyProperty", "DependencyProperty"),
            ("Style", "Style"),
            ("Event", "Event"),
            ("Interaction", "interaction"),
            ("Layout", "Layout"),
            ("MVVM", "MVVM"),
            ("Performance", "Performance")
        };

        foreach (var (category, keyword) in categoryKeywords)
        {
            ServerInstructions.Value.Should().Contain(keyword,
                $"ServerInstructions should mention '{category}' tool category (checking for '{keyword}')");
        }
    }

    [Fact]
    public void Value_ShouldHaveValidStructure()
    {
        // Check for section headers with === markers
        ServerInstructions.Value.Should().Contain("===");

        // Check for workflow examples
        ServerInstructions.Value.Should().Contain("Workflow 1");
        ServerInstructions.Value.Should().Contain("Workflow 2");
        ServerInstructions.Value.Should().Contain("Workflow 3");
        ServerInstructions.Value.Should().Contain("Workflow 4");
    }

    [Fact]
    public void Value_ShouldBeReasonablyLong()
    {
        // ServerInstructions should be comprehensive (at least 2000 characters)
        ServerInstructions.Value.Length.Should().BeGreaterThan(2000,
            "ServerInstructions should provide comprehensive guidance");
    }
}
