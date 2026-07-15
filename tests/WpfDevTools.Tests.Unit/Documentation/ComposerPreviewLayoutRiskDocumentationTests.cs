using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ComposerPreviewLayoutRiskDocumentationTests
{
    [Theory]
    [InlineData("docfx/reference/tools/ui-composer.md")]
    [InlineData("docfx/zh-tw/reference/tools/ui-composer.md")]
    public void ComposerReference_ShouldDocumentRuntimeClippingSummary(string path)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(path));

        content.Should().Contain("layoutRiskSummary");
        content.Should().Contain("get_clipping_info");
        content.Should().Contain("jsonPath");
        content.Should().Contain("blockKind");
        content.Should().ContainAll(
            "correlatedTargetCount",
            "resolvedTargetCount",
            "inspectedTargetCount",
            "inspectionTruncated",
            "unresolvedCorrelationCount",
            "reportedUnresolvedCorrelationCount",
            "unresolvedCorrelationsTruncated",
            "unresolvedCorrelations",
            "uninspectedCorrelationCount",
            "reportedUninspectedCorrelationCount",
            "uninspectedCorrelationsTruncated",
            "uninspectedCorrelations",
            "warningsTruncated",
            "ambiguous",
            "searchComplete=false");
    }

    [Theory]
    [InlineData("docfx/reference/tools/interaction-events-layout.md")]
    [InlineData("docfx/zh-tw/reference/tools/interaction-events-layout.md")]
    public void LayoutReference_ShouldDocumentExplicitClippingBatch(string path)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(path));

        content.Should().Contain("elementIds");
        content.Should().Contain("100");
        content.Should().Contain("elementId");
    }
}
