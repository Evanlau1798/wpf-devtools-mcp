using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolDescriptionMaintainabilityTests
{
    [Fact]
    public void ToolWrapperFiles_ShouldReferenceDescriptionConstantsInsteadOfInlineBlocks()
    {
        var violations = GetToolWrapperFiles()
            .SelectMany(relativePath => File.ReadLines(TestRepositoryPaths.GetRepoFilePath(relativePath))
                .Select((line, index) => new
                {
                    Path = relativePath,
                    Line = index + 1,
                    Text = line.Trim()
                }))
            .Where(line => line.Text == "[Description(")
            .Select(line => $"{line.Path}:{line.Line}")
            .ToArray();

        violations.Should().BeEmpty(
            "method-level MCP tool descriptions should live in companion const files so wrappers stay readable");
    }

    [Fact]
    public void ToolWrapperFiles_ShouldHaveDescriptionCompanionFiles()
    {
        var missingCompanions = GetToolWrapperFiles()
            .Select(relativePath => new
            {
                ToolWrapper = relativePath,
                Companion = relativePath.Replace("McpTools.cs", "McpToolDescriptions.cs", StringComparison.Ordinal)
            })
            .Where(pair => !File.Exists(TestRepositoryPaths.GetRepoFilePath(pair.Companion)))
            .Select(pair => $"{pair.ToolWrapper} -> {pair.Companion}")
            .ToArray();

        missingCompanions.Should().BeEmpty(
            "long MCP tool descriptions should be reviewable beside each wrapper in generated/companion const files");
    }

    private static IReadOnlyList<string> GetToolWrapperFiles()
    {
        var toolsDirectory = TestRepositoryPaths.GetRepoFilePath("src/WpfDevTools.Mcp.Server/McpTools");
        return Directory.GetFiles(toolsDirectory, "*McpTools.cs", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetRelativePath(TestRepositoryPaths.GetRepoFilePath("."), path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
