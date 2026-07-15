using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ComposerCompositionIdentityDocumentationTests
{
    [Theory]
    [InlineData("docfx/reference/tools/ui-composer.md")]
    [InlineData("docfx/zh-tw/reference/tools/ui-composer.md")]
    public void ComposerReference_ShouldDocumentSameCallStandardIdentity(string path)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(path));

        var compositionSection = content.Split("## `compose_ui_blueprint`")[1]
            .Split("## `validate_ui_blueprint`")[0];
        compositionSection.Should().ContainAll(
            "`elementName`",
            "`automationId`",
            "`insertedNodeSummary`");
    }

    [Theory]
    [InlineData("docfx/reference/tools/ui-composer.md")]
    [InlineData("docfx/zh-tw/reference/tools/ui-composer.md")]
    public void ComposerReference_ShouldDocumentExtensionDeclaredSlotCapacity(string path)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(path));

        var compositionSection = content.Split("## `compose_ui_blueprint`")[1]
            .Split("## `validate_ui_blueprint`")[0];
        compositionSection.Should().ContainAll(
            "`minItems`",
            "`maxItems`",
            "`targetSlotSummary`",
            "`remainingCapacity`",
            "JSON null");
    }

    [Theory]
    [InlineData("docfx/reference/tools/ui-composer.md")]
    [InlineData("docfx/zh-tw/reference/tools/ui-composer.md")]
    public void ComposerReference_ShouldDocumentBlueprintShapeRecovery(string path)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(path));

        content.Should().ContainAll(
            "InvalidBlueprintShape",
            "`observedValueKind`",
            "`expectedJsonShape`");
    }
}
