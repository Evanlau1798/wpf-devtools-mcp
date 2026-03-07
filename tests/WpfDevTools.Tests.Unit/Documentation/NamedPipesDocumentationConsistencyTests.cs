using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class NamedPipesDocumentationConsistencyTests
{
    [Theory]
    [InlineData("AGENTS.md")]
    [InlineData("docs/NAMED_PIPES_IPC_GUIDE.md")]
    [InlineData("docs/architecture/ADR-001-named-pipes-for-ipc.md")]
    [InlineData("docs/architecture/ADR-003-length-prefix-framing.md")]
    [InlineData("docs/checklist.md")]
    public void Documentation_ShouldDescribeCurrentByteModeImplementation(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().ContainEquivalentOf("byte",
            $"{relativePath} should describe the current byte-mode Named Pipe implementation used by InspectorHost");
        content.Should().ContainEquivalentOf("length-prefix",
            $"{relativePath} should continue to describe the framing contract accurately");
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
