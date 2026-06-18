using System.Reflection;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class ProgramRegistrationTests
{
    [Fact]
    public void Program_ShouldRegisterToolsPromptsAndResources_FromAssembly()
    {
        var programPath = Path.Combine(FindSolutionRoot(), "src", "WpfDevTools.Mcp.Server", "Program.cs");
        var content = File.ReadAllText(programPath);

        content.Should().Contain(".WithToolsFromAssembly(");
        content.Should().Contain(".WithPromptsFromAssembly(",
            "Claude Code prompt discovery needs MCP prompt registration");
        content.Should().Contain(".WithResourcesFromAssembly(",
            "Claude Code @resource discovery needs MCP resource registration");
    }

    [Fact]
    public void Program_CallToolFilter_ShouldRejectOversizedToolNamesBeforePolicyDispatch()
    {
        var programPath = Path.Combine(FindSolutionRoot(), "src", "WpfDevTools.Mcp.Server", "Program.cs");
        var content = File.ReadAllText(programPath);

        content.Should().Contain("parameters?.Name is { Length: > BoundaryStringLimits.MaxInspectorMethodLength }",
            "the MCP server owns the tool-call boundary after the SDK parses the JSON-RPC envelope");
        content.Should().Contain("ToolCallHelper.CreateStructuredErrorResult");
        content.Should().Contain("\"InvalidArgument\"");

        var guardIndex = content.IndexOf("BoundaryStringLimits.MaxInspectorMethodLength", StringComparison.Ordinal);
        var policyIndex = content.IndexOf("toolPolicy.EvaluateToolCall", StringComparison.Ordinal);

        guardIndex.Should().BeGreaterThanOrEqualTo(0);
        policyIndex.Should().BeGreaterThanOrEqualTo(0);
        guardIndex.Should().BeLessThan(policyIndex,
            "oversized names should be rejected before allowlist policy evaluation or dispatch");
    }

    [Fact]
    public void Program_CallToolFilter_ShouldValidateArgumentsBeforePolicyDispatch()
    {
        var programPath = Path.Combine(FindSolutionRoot(), "src", "WpfDevTools.Mcp.Server", "Program.cs");
        var content = File.ReadAllText(programPath);

        content.Should().Contain("McpToolArgumentValidator.Validate(parameters?.Name, parameters?.Arguments)",
            "raw SDK call-tool parameters must be checked before typed tool wrappers can discard unknown arguments");

        var validationIndex = content.IndexOf("McpToolArgumentValidator.Validate", StringComparison.Ordinal);
        var policyIndex = content.IndexOf("toolPolicy.EvaluateToolCall", StringComparison.Ordinal);

        validationIndex.Should().BeGreaterThanOrEqualTo(0);
        policyIndex.Should().BeGreaterThanOrEqualTo(0);
        validationIndex.Should().BeLessThan(policyIndex,
            "argument validation should reject invalid requests before policy dispatch or wrapper binding");
    }

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate solution root for Program registration tests.");
    }
}
