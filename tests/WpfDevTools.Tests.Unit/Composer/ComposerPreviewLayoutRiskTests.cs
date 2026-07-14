using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewLayoutRiskTests
{
    [Fact]
    public void BuildClippingTargetIds_ShouldReturnDistinctCorrelatedElements()
    {
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                results = new[]
                {
                    new { elementId = "Button_7", elementName = "ActionButton" },
                    new { elementId = "Button_7", elementName = "ActionButton" },
                    new { elementId = "TextBlock_8", elementName = "ActionLabel" }
                }
            })
        };

        var targets = UiBlueprintPreviewDiagnosticsBridge.BuildClippingTargetIds(diagnostics);

        targets.Should().Equal("Button_7", "TextBlock_8");
    }

    [Fact]
    public void Analyze_ShouldMapClippedElementToExactBlueprintPath()
    {
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                results = new[]
                {
                    new { elementId = "Button_7", elementName = "ActionButton" }
                }
            }),
            Diagnostic("get_clipping_info", new
            {
                success = true,
                resultCount = 1,
                successCount = 1,
                failureCount = 0,
                results = new[]
                {
                    new
                    {
                        success = true,
                        elementId = "Button_7",
                        isClipped = true,
                        clippingSource = "ancestor-layout-clip",
                        overflowAmount = new { left = 0, top = 0, right = 50, bottom = 0 },
                        suggestedFix = "Increase the available layout slot."
                    }
                }
            })
        };
        var correlations = new[]
        {
            new RenderElementCorrelation(
                "ActionButton",
                "$.layout.slots.actions[2]",
                "nebula.button")
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.ClippedElementCount.Should().Be(1);
        summary.ReportedWarningCount.Should().Be(1);
        summary.Warnings.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Code = "RuntimeClippingDetected",
            JsonPath = "$.layout.slots.actions[2]",
            BlockKind = "nebula.button",
            ElementName = "ActionButton",
            ElementId = "Button_7",
            ClippingSource = "ancestor-layout-clip",
            SuggestedFix = "Increase the available layout slot."
        }, options => options.ExcludingMissingMembers());
        summary.Warnings[0].OverflowAmount.GetProperty("right").GetDouble().Should().Be(50);
    }

    [Fact]
    public void Analyze_ShouldCorrelateSingleTargetResponseWithoutBatchEnvelope()
    {
        var clipping = Diagnostic("get_clipping_info", new
        {
            success = true,
            isClipped = true,
            clippingSource = "layout-clip",
            overflowAmount = new { left = 0, top = 0, right = 8, bottom = 0 }
        }) with
        {
            TargetElementIds = ["TextBlock_2"]
        };
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                results = new[] { new { elementId = "TextBlock_2", elementName = "Title" } }
            }),
            clipping
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(
            diagnostics,
            [new RenderElementCorrelation("Title", "$.layout", "nebula.text")]);

        summary.Warnings.Should().ContainSingle()
            .Which.ElementId.Should().Be("TextBlock_2");
    }

    private static PreviewRuntimeDiagnostic Diagnostic(string tool, object payload)
        => new(tool, Success: true, JsonSerializer.SerializeToElement(payload));
}
