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
