using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewLayoutAttentionTests
{
    [Fact]
    public void Analyze_ShouldSurfaceSliverAndScrollContextAsActionableSummary()
    {
        var scrollContext = new
        {
            elementId = "ScrollViewer_2",
            elementName = "CardRail",
            viewportWidth = 800,
            extentWidth = 1240,
            horizontalOverflow = true,
            horizontalScrollBarVisibility = "Hidden"
        };
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                results = new[] { new { elementId = "Border_7", elementName = "TrailingCard" } }
            }),
            Diagnostic("get_clipping_info", new
            {
                success = true,
                results = new[]
                {
                    new
                    {
                        success = true,
                        elementId = "Border_7",
                        isClipped = true,
                        clippingSource = "ancestor-layout-clip",
                        visibleContentImpact = "not-determined",
                        geometricClippingSeverity = "partial",
                        visibleRatio = 0.08,
                        nearestScrollContainer = scrollContext,
                        overflowAmount = new { left = 0, top = 0, right = 92, bottom = 0 },
                        suggestedFix = "Review the rail geometry."
                    }
                }
            })
        };
        var correlations = new[]
        {
            new RenderElementCorrelation("TrailingCard", "$.layout.slots.children[6]", "core.border")
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.AttentionRequiredCount.Should().Be(1);
        summary.MinimumVisibleRatio.Should().Be(0.08);
        var warning = summary.Warnings.Should().ContainSingle().Subject;
        warning.VisibilityClassification.Should().Be("sliver");
        warning.RequiresAttention.Should().BeTrue();
        warning.NearestScrollContainer.Should().NotBeNull();
        warning.NearestScrollContainer!.Value.GetProperty("elementName").GetString().Should().Be("CardRail");
    }

    [Fact]
    public void Analyze_WithoutClipping_ShouldNotInventVisibilityRatio()
    {
        var summary = PreviewLayoutRiskAnalyzer.Analyze([], []);

        summary.AttentionRequiredCount.Should().Be(0);
        summary.MinimumVisibleRatio.Should().BeNull();
    }

    [Fact]
    public void Analyze_FullyHiddenTarget_ShouldRequireAttention()
    {
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                results = new[] { new { elementId = "Border_9", elementName = "HiddenCard" } }
            }),
            Diagnostic("get_clipping_info", new
            {
                success = true,
                results = new[]
                {
                    new
                    {
                        success = true,
                        elementId = "Border_9",
                        isClipped = true,
                        clippingSource = "ancestor-layout-clip",
                        visibleContentImpact = "not-determined",
                        geometricClippingSeverity = "full",
                        visibleRatio = 0.0
                    }
                }
            })
        };
        var correlations = new[]
        {
            new RenderElementCorrelation("HiddenCard", "$.layout.slots.children[8]", "core.border")
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.AttentionRequiredCount.Should().Be(1);
        var warning = summary.Warnings.Should().ContainSingle().Subject;
        warning.VisibilityClassification.Should().Be("hidden");
        warning.RequiresAttention.Should().BeTrue();
    }

    [Fact]
    public void Analyze_HiddenScrollableTarget_ShouldRemainContextOnly()
    {
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                results = new[] { new { elementId = "Border_11", elementName = "OffscreenCard" } }
            }),
            Diagnostic("get_clipping_info", new
            {
                success = true,
                results = new[]
                {
                    new
                    {
                        success = true,
                        elementId = "Border_11",
                        isClipped = true,
                        clippingSource = "ancestor-layout-clip",
                        visibleContentImpact = "not-determined",
                        geometricClippingSeverity = "full",
                        visibleRatio = 0.0,
                        nearestScrollContainer = new
                        {
                            canBringTargetIntoView = true,
                            hasVisibleScrollBarChrome = false
                        }
                    }
                }
            })
        };
        var correlations = new[]
        {
            new RenderElementCorrelation("OffscreenCard", "$.layout.slots.children[10]", "core.border")
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.AttentionRequiredCount.Should().Be(0);
        summary.MinimumVisibleRatio.Should().BeNull();
        summary.Warnings.Should().ContainSingle().Which.RequiresAttention.Should().BeFalse();
    }

    [Fact]
    public void Analyze_WhenWarningsAreTruncated_ShouldKeepAttentionRequiredTargets()
    {
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                results = Enumerable.Range(0, 33)
                    .Select(index => new { elementId = $"Border_{index}", elementName = $"Card{index}" })
            }),
            Diagnostic("get_clipping_info", new
            {
                success = true,
                results = Enumerable.Range(0, 33)
                    .Select(index => new
                    {
                        success = true,
                        elementId = $"Border_{index}",
                        isClipped = true,
                        clippingSource = "ancestor-layout-clip",
                        visibleContentImpact = "not-determined",
                        geometricClippingSeverity = index == 32 ? "full" : "partial",
                        visibleRatio = index == 32 ? 0.0 : 0.5
                    })
            })
        };
        var correlations = Enumerable.Range(0, 33)
            .Select(index => new RenderElementCorrelation(
                $"Card{index}",
                $"$.layout.slots.children[{index}]",
                "core.border"))
            .ToArray();

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.WarningsTruncated.Should().BeTrue();
        summary.AttentionRequiredCount.Should().Be(1);
        summary.Warnings.Should().Contain(warning =>
            warning.JsonPath == "$.layout.slots.children[32]" && warning.RequiresAttention);
    }

    private static PreviewRuntimeDiagnostic Diagnostic(string tool, object payload)
        => new(tool, Success: true, JsonSerializer.SerializeToElement(payload));
}
