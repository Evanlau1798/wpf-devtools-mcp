using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewPayloadCompactionTests
{
    [Theory]
    [InlineData(1, 0, 0, false)]
    [InlineData(0, 1, 0, false)]
    [InlineData(0, 0, 1, false)]
    [InlineData(0, 0, 0, true)]
    public void ShouldCompactElementCorrelations_ShouldPreserveDetailsWhenLayoutRiskExists(
        int clippedCount,
        int unresolvedCount,
        int uninspectedCount,
        bool inspectionTruncated)
    {
        var risk = PreviewLayoutRiskSummary.Empty with
        {
            ClippedElementCount = clippedCount,
            UnresolvedCorrelationCount = unresolvedCount,
            UninspectedCorrelationCount = uninspectedCount,
            InspectionTruncated = inspectionTruncated
        };

        UiComposerMcpTools.ShouldCompactElementCorrelations(
            compactSuccessfulPayload: true,
            risk).Should().BeFalse();
    }

    [Fact]
    public void ShouldCompactElementCorrelations_ShouldCompactRiskFreeSuccessfulPayload()
    {
        UiComposerMcpTools.ShouldCompactElementCorrelations(
            compactSuccessfulPayload: true,
            PreviewLayoutRiskSummary.Empty).Should().BeTrue();
    }
}
