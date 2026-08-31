using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewLayoutOverflowAttentionTests
{
    [Fact]
    public void Analyze_PartialOverflowBeyondTwoDip_ShouldRequireAttention()
    {
        var summary = AnalyzeSingle(overflow: 20, canBringTargetIntoView: false);

        summary.AttentionRequiredCount.Should().Be(1);
        summary.Warnings.Should().ContainSingle().Which.RequiresAttention.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Analyze_PartialOverflowAtMostTwoDip_ShouldRemainRoundingNoise(double overflow)
    {
        var summary = AnalyzeSingle(overflow, canBringTargetIntoView: false);

        summary.AttentionRequiredCount.Should().Be(0);
        summary.Warnings.Should().ContainSingle().Which.RequiresAttention.Should().BeFalse();
    }

    [Fact]
    public void Analyze_ScrollReachablePartialOverflow_ShouldNotRequireAttention()
    {
        var summary = AnalyzeSingle(overflow: 20, canBringTargetIntoView: true);

        summary.AttentionRequiredCount.Should().Be(0);
        summary.Warnings.Should().ContainSingle().Which.RequiresAttention.Should().BeFalse();
    }

    [Fact]
    public void Analyze_WarningCap_ShouldSelectAttentionWarningsBeforeNoise()
    {
        var correlations = Enumerable.Range(0, 33)
            .Select(index => new RenderElementCorrelation(
                $"Card{index}",
                $"$.layout.slots.children[{index}]",
                "core.border"))
            .ToArray();
        var matches = correlations.Select((item, index) => new
        {
            elementId = $"Border_{index}",
            elementName = item.ElementName
        });
        var clipping = correlations.Select((_, index) => new
        {
            success = true,
            elementId = $"Border_{index}",
            isClipped = true,
            clippingSource = "explicit-clip",
            visibleContentImpact = "not-determined",
            geometricClippingSeverity = "partial",
            visibleRatio = index == 0 ? 0.2 : 0.5,
            overflowAmount = new { left = 0, top = 0, right = index == 0 ? 2 : 20, bottom = 0 }
        });
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new { success = true, results = matches }),
            Diagnostic("get_clipping_info", new { success = true, results = clipping })
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.AttentionRequiredCount.Should().Be(32);
        summary.Warnings.Should().HaveCount(32)
            .And.OnlyContain(warning => warning.RequiresAttention);
    }

    private static PreviewLayoutRiskSummary AnalyzeSingle(
        double overflow,
        bool canBringTargetIntoView)
    {
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                results = new[] { new { elementId = "Border_1", elementName = "FeatureCard" } }
            }),
            Diagnostic("get_clipping_info", new
            {
                success = true,
                results = new[]
                {
                    new
                    {
                        success = true,
                        elementId = "Border_1",
                        isClipped = true,
                        clippingSource = "explicit-clip",
                        visibleContentImpact = "not-determined",
                        geometricClippingSeverity = "partial",
                        visibleRatio = 0.5,
                        nearestScrollContainer = new { canBringTargetIntoView },
                        overflowAmount = new { left = 0, top = 0, right = overflow, bottom = 0 }
                    }
                }
            })
        };
        var correlations = new[]
        {
            new RenderElementCorrelation("FeatureCard", "$.layout", "core.border")
        };

        return PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);
    }

    private static PreviewRuntimeDiagnostic Diagnostic(string tool, object payload)
        => new(tool, Success: true, JsonSerializer.SerializeToElement(payload));
}
