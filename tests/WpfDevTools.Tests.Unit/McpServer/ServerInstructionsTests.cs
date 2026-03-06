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
    public void Value_ShouldContainLimitations()
    {
        ServerInstructions.Value.Should().Contain("LIMITATIONS");
    }
}
