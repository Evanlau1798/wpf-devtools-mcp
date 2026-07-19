using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewRuntimeRealizationTests
{
    [Fact]
    public void Analyze_ShouldClassifyAuthoredElementAbsentFromCompletedSearchAsNotRealized()
    {
        var diagnostics = new[]
        {
            new PreviewRuntimeDiagnostic(
                "find_elements",
                Success: true,
                JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    searchComplete = true,
                    results = Array.Empty<object>()
                }))
            {
                Lookup = new PreviewCorrelationLookup("DeferredAction", "exact")
            }
        };
        var correlations = new[]
        {
            new RenderElementCorrelation(
                "DeferredAction",
                "$.layout.slots.items[1].slots.content[0]",
                "aurora.action")
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.UnresolvedCorrelations.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Reason = "runtime-not-realized",
            RequiresActiveStateInspection = true
        });
    }
}
