using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewLayoutRiskClassificationTests
{
    [Theory]
    [InlineData("layout-clip", "RuntimeStructuralOverflowRisk", "structural-overflow")]
    [InlineData("ancestor-layout-clip", "RuntimeStructuralOverflowRisk", "structural-overflow")]
    [InlineData("explicit-clip", "RuntimeClippingDetected", "clipping")]
    public void Analyze_ShouldClassifyUnconfirmedRuntimeLayoutRisks(
        string clippingSource,
        string expectedCode,
        string expectedRiskClassification)
    {
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                results = new[] { new { elementId = "Border_7", elementName = "FeatureCard" } }
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
                        clippingSource,
                        visibleContentImpact = "not-determined",
                        overflowAmount = new { left = 0, top = 0, right = 0, bottom = 22 },
                        suggestedFix = "Confirm affected content with focused checks or a screenshot."
                    }
                }
            })
        };
        var correlations = new[]
        {
            new RenderElementCorrelation("FeatureCard", "$.layout", "orbit.card")
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        var warning = summary.Warnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(expectedCode);
        var payload = JsonSerializer.SerializeToElement(warning);
        payload.GetProperty("RiskClassification").GetString().Should().Be(expectedRiskClassification);
        payload.GetProperty("Severity").GetString().Should().Be("advisory");
        payload.GetProperty("VisibleContentImpact").GetString().Should().Be("not-determined");
        payload.GetProperty("RequiresVisualConfirmation").GetBoolean().Should().BeTrue();
    }

    private static PreviewRuntimeDiagnostic Diagnostic(string tool, object payload)
        => new(tool, Success: true, JsonSerializer.SerializeToElement(payload));
}
