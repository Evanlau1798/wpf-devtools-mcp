using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ComposerAdjacencyAdvisoryDocumentationTests
{
    [Theory]
    [InlineData("docfx/reference/tools/ui-composer.md")]
    [InlineData("docfx/zh-tw/reference/tools/ui-composer.md")]
    public void ComposerReference_ShouldDocumentExtensionDeclaredAdjacencyAdvisories(string path)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(path));

        content.Should().ContainAll(
            "authoringRoles",
            "adjacencyAdvisory",
            "childRole",
            "whenProperty",
            "whenValues",
            "itemSpacingProperty",
            "childMarginProperty",
            "AdjacentContentWithoutSeparation");
        content.Should().Contain("32");
    }
}
