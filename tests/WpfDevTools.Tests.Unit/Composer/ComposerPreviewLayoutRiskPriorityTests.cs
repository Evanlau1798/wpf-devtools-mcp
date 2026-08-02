using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewLayoutRiskPriorityTests
{
    [Fact]
    public void Analyze_ShouldPrioritizePartiallyVisibleRiskOverFullyOffscreenOverflow()
    {
        var correlations = Enumerable.Range(0, 33)
            .Select(index => new RenderElementCorrelation($"Element{index}", $"$.layout.slots.children[{index}]", "sample.card"))
            .ToArray();
        var matches = correlations.Select((item, index) => new
        {
            elementId = $"id-{index}",
            elementName = item.ElementName
        }).ToArray();
        var clipping = correlations.Select((_, index) => new
        {
            success = true,
            elementId = $"id-{index}",
            isClipped = true,
            visibleContentImpact = "not-determined",
            clippingSource = "ancestor-layout-clip",
            geometricClippingSeverity = index == 32 ? "partial" : "full",
            visibleRatio = index == 32 ? 0.25 : 0,
            overflowAmount = new { left = 0, top = 0, right = 10, bottom = 0 },
            suggestedFix = "Inspect the viewport."
        }).ToArray();
        var diagnostics = new PreviewRuntimeDiagnostic[]
        {
            new("find_elements", true, JsonSerializer.SerializeToElement(new { results = matches })),
            new("get_clipping_info", true, JsonSerializer.SerializeToElement(new { results = clipping }))
            {
                TargetElementIds = matches.Select(item => item.elementId).ToArray()
            }
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.Warnings.Should().HaveCount(32);
        summary.Warnings[0].ElementName.Should().Be("Element32");
        var firstWarning = JsonSerializer.SerializeToElement(summary.Warnings[0]);
        firstWarning.GetProperty("GeometricClippingSeverity").GetString().Should().Be("partial");
        firstWarning.GetProperty("VisibleRatio").GetDouble().Should().Be(0.25);
        summary.WarningsTruncated.Should().BeTrue();
    }
}
