using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ComposerWpfUiEditorialDocumentationTests
{
    [Theory]
    [InlineData("docfx/reference/tools/ui-composer.md")]
    [InlineData("docfx/zh-tw/reference/tools/ui-composer.md")]
    public void ComposerReference_ShouldExplainPackOwnedEditorialMediaContract(string path)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(path));

        content.Should().ContainAll(
            "wpfui.editorialCard",
            "mediaSource",
            "binding",
            "symbol fallback");
        content.Should().Contain("no Composer engine special case");
    }
}
