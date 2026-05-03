using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class ConnectToolMaintainabilityTests
{
    [Fact]
    public void ExecuteForProcessAsync_ShouldRemainAnOrchestrator()
    {
        var sourcePath = TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Mcp.Server/Tools/ConnectTool.cs");
        var lines = File.ReadAllLines(sourcePath);

        var methodLineCount = CountMethodLines(lines, "ExecuteForProcessAsync");

        methodLineCount.Should().BeLessThanOrEqualTo(80,
            "connect orchestration should delegate validation, injection, and pipe handshake responsibilities to focused helpers");
    }

    private static int CountMethodLines(string[] lines, string methodName)
    {
        var start = Array.FindIndex(lines, line => line.Contains(methodName, StringComparison.Ordinal));
        start.Should().BeGreaterThanOrEqualTo(0);

        var braceDepth = 0;
        var sawOpeningBrace = false;
        for (var index = start; index < lines.Length; index++)
        {
            foreach (var ch in lines[index])
            {
                if (ch == '{')
                {
                    braceDepth++;
                    sawOpeningBrace = true;
                }
                else if (ch == '}')
                {
                    braceDepth--;
                    if (sawOpeningBrace && braceDepth == 0)
                    {
                        return index - start + 1;
                    }
                }
            }
        }

        throw new InvalidOperationException($"Could not find end of method '{methodName}'.");
    }
}
