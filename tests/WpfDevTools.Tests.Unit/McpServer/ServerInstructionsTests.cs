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
        ServerInstructions.Value.Should().Contain("get_processes");
        ServerInstructions.Value.Should().Contain("connect");
    }

    [Fact]
    public void Value_ShouldContainParameterConventions()
    {
        ServerInstructions.Value.Should().Contain("PARAMETER CONVENTIONS");
        ServerInstructions.Value.Should().Contain("processId");
        ServerInstructions.Value.Should().Contain("elementId");
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
        ServerInstructions.Value.Should().Contain("100 requests/minute");
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
    }

    [Fact]
    public void Value_ShouldContainLimitations()
    {
        ServerInstructions.Value.Should().Contain("LIMITATIONS");
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
