using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewVisualLayoutToleranceTests
{
    [Fact]
    public void Analyze_NarrowRegionLargeProportionalSizeError_ShouldFailWithReason()
    {
        var summary = Analyze(
            expected: new PreviewNormalizedBounds(0.1, 0.1, 0.02, 0.02),
            actualX: 100,
            actualY: 100,
            actualWidth: 40,
            actualHeight: 20,
            tolerance: 0.05);

        summary.Passed.Should().BeFalse();
        var region = summary.Regions.Should().ContainSingle().Subject;
        region.Status.Should().Be("mismatch");
        region.Reason.Should().Contain("width");
    }

    [Fact]
    public void Analyze_NarrowRegionTwoDipSizeDifferencePerAxis_ShouldPass()
    {
        var summary = Analyze(
            expected: new PreviewNormalizedBounds(0.1, 0.1, 0.004, 0.005),
            actualX: 100,
            actualY: 100,
            actualWidth: 6,
            actualHeight: 7,
            tolerance: 0.05);

        summary.Passed.Should().BeTrue();
        summary.Regions.Should().ContainSingle().Which.Reason.Should().BeNull();
    }

    [Fact]
    public void Analyze_NarrowRegionPosition_ShouldKeepAbsoluteTolerance()
    {
        var summary = Analyze(
            expected: new PreviewNormalizedBounds(0.1, 0.1, 0.004, 0.005),
            actualX: 130,
            actualY: 130,
            actualWidth: 4,
            actualHeight: 5,
            tolerance: 0.04);

        summary.Passed.Should().BeTrue();
    }

    private static PreviewVisualLayoutContractSummary Analyze(
        PreviewNormalizedBounds expected,
        double actualX,
        double actualY,
        double actualWidth,
        double actualHeight,
        double tolerance)
    {
        var contract = new PreviewVisualLayoutContract(
        [
            new PreviewVisualLayoutRegion(
                "NarrowRegion",
                expected,
                tolerance,
                "any",
                "any")
        ]);
        var diagnostics = new[]
        {
            Diagnostic("get_layout_info", new
            {
                success = true,
                actualWidth = 1000,
                actualHeight = 1000,
                positionInWindow = new { x = 0, y = 0 }
            }),
            Diagnostic("get_element_snapshot", new
            {
                success = true,
                elementId = "Border_1",
                elementName = "NarrowRegion",
                properties = new { },
                layout = new
                {
                    actualWidth,
                    actualHeight,
                    positionInWindow = new { x = actualX, y = actualY }
                }
            })
        };

        return PreviewVisualLayoutContractAnalyzer.Analyze(diagnostics, contract);
    }

    private static PreviewRuntimeDiagnostic Diagnostic(string tool, object payload)
        => new(tool, Success: true, JsonSerializer.SerializeToElement(payload));
}
