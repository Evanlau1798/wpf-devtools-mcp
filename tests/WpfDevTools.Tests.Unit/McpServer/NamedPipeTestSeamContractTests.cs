using FluentAssertions;
using System.Reflection;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class NamedPipeTestSeamContractTests
{
    [Fact]
    public void SessionManagerPipeClients_ShouldRemainPrivate()
    {
        var field = typeof(SessionManager).GetField(
            "_pipeClients",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        field.Should().NotBeNull();
        field!.IsPrivate.Should().BeTrue(
            "tests should use explicit SessionManager test seams instead of manipulating pipe-client storage directly");
    }

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

    [Fact]
    public void UnitTests_ShouldUseExplicitSessionManagerTestSeamsInsteadOfDirectPipeClientDictionaryAccess()
    {
        var testRoot = TestRepositoryPaths.GetRepoFilePath("tests/WpfDevTools.Tests.Unit");

        var directDictionaryUsages = Directory
            .EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(nameof(NamedPipeTestSeamContractTests) + ".cs", StringComparison.Ordinal))
            .SelectMany(EnumerateDirectPipeClientDictionaryUsages)
            .OrderBy(usage => usage, StringComparer.Ordinal)
            .ToArray();

        directDictionaryUsages.Should().BeEmpty(
            "tests should use TestHelpers.ReplaceSessionManagerPipeClient instead of coupling to SessionManager's pipe-client dictionary");
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

    private static IEnumerable<string> EnumerateDirectPipeClientDictionaryUsages(string path)
    {
        var relativePath = Path.GetRelativePath(
            TestRepositoryPaths.GetRepoFilePath("."),
            path).Replace('\\', '/');

        foreach (var line in File.ReadLines(path).Select((text, index) => new { Text = text, Number = index + 1 }))
        {
            if (line.Text.Contains("._pipeClients", StringComparison.Ordinal))
            {
                yield return $"{relativePath}:{line.Number}: {line.Text.Trim()}";
            }
        }
    }
}
