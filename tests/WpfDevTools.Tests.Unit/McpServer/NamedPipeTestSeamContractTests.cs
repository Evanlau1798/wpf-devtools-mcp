using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class NamedPipeTestSeamContractTests
{
    [Fact]
    public void McpServerTests_ShouldUseExplicitSessionManagerTestSeamsInsteadOfReflection()
    {
        var mcpServerTestRoot = TestRepositoryPaths.GetRepoFilePath("tests/WpfDevTools.Tests.Unit/McpServer");

        var reflectionUsages = Directory
            .EnumerateFiles(mcpServerTestRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(nameof(NamedPipeTestSeamContractTests) + ".cs", StringComparison.Ordinal))
            .SelectMany(EnumerateForbiddenReflectionUsages)
            .OrderBy(usage => usage, StringComparer.Ordinal)
            .ToArray();

        reflectionUsages.Should().BeEmpty(
            "named-pipe test harnesses should use TestHelpers.ReplaceSessionManagerPipeClient and DisableSessionManagerCleanupTimer rather than reflecting over SessionManager internals");
    }

    private static IEnumerable<string> EnumerateForbiddenReflectionUsages(string path)
    {
        var relativePath = Path.GetRelativePath(
            TestRepositoryPaths.GetRepoFilePath("."),
            path).Replace('\\', '/');
        var forbiddenPatterns = new[]
        {
            "GetField(\"_pipeClients\"",
            "GetField(\"_cleanupTimer\""
        };

        foreach (var line in File.ReadLines(path).Select((text, index) => new { Text = text, Number = index + 1 }))
        {
            if (forbiddenPatterns.Any(pattern => line.Text.Contains(pattern, StringComparison.Ordinal)))
            {
                yield return $"{relativePath}:{line.Number}: {line.Text.Trim()}";
            }
        }
    }
}
