using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewNameScopeCoverageTests
{
    [Fact]
    public void Analyze_ShouldClassifyNamescopeOnlyTargetsWithoutTruncatingVisualInspection()
    {
        var correlations = new[]
        {
            new RenderElementCorrelation("ActiveTarget", "$.layout.slots.items[0]", "sample.item"),
            new RenderElementCorrelation("InactiveTarget", "$.layout.slots.items[1]", "sample.item")
        };
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                searchComplete = true,
                results = new[] { new { elementId = "Element_Active", elementName = "ActiveTarget" } }
            }),
            Diagnostic("get_namescope", new
            {
                success = true,
                traversalTruncated = false,
                namedElements = new[]
                {
                    new { elementId = "Element_Active", name = "ActiveTarget", type = "Button" },
                    new { elementId = "Element_Inactive", name = "InactiveTarget", type = "Button" }
                }
            }),
            Diagnostic("get_clipping_info", new
            {
                success = true,
                results = new[]
                {
                    new { success = true, elementId = "Element_Active", isClipped = false }
                }
            }) with { TargetElementIds = ["Element_Active"] }
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.CorrelatedTargetCount.Should().Be(2);
        summary.ResolvedTargetCount.Should().Be(2);
        summary.InspectedTargetCount.Should().Be(1);
        summary.InspectionTruncated.Should().BeFalse();
        summary.UnresolvedCorrelationCount.Should().Be(0);
        summary.NamescopeOnlyCorrelationCount.Should().Be(1);
        summary.NamescopeOnlyCorrelations.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            JsonPath = "$.layout.slots.items[1]",
            BlockKind = "sample.item",
            ElementName = "InactiveTarget",
            ElementId = "Element_Inactive",
            Reason = "namescope-only"
        });
    }

    [Fact]
    public void Analyze_ShouldPreserveLookupBudgetWhenNamescopeContainsUnsearchedTarget()
    {
        var correlations = new[]
        {
            new RenderElementCorrelation("AlphaTarget", "$.layout.slots.items[0]", "sample.item"),
            new RenderElementCorrelation("ZetaTarget", "$.layout.slots.items[1]", "sample.item")
        };
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                searchComplete = true,
                results = new[] { new { elementId = "Element_Alpha", elementName = "AlphaTarget" } }
            }) with { Lookup = new PreviewCorrelationLookup("AlphaTarget", "exact") },
            Diagnostic("get_namescope", new
            {
                namedElements = new[]
                {
                    new { elementId = "Element_Alpha", name = "AlphaTarget" },
                    new { elementId = "Element_Zeta", name = "ZetaTarget" }
                }
            }),
            Diagnostic("get_clipping_info", new
            {
                results = new[] { new { success = true, elementId = "Element_Alpha", isClipped = false } }
            }) with { TargetElementIds = ["Element_Alpha"] }
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations, correlationLookupLimit: 1);

        summary.ResolvedTargetCount.Should().Be(1);
        summary.NamescopeOnlyCorrelationCount.Should().Be(0);
        summary.InspectionTruncated.Should().BeTrue();
        summary.UnresolvedCorrelations.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            JsonPath = "$.layout.slots.items[1]",
            BlockKind = "sample.item",
            ElementName = "ZetaTarget",
            Reason = "lookup-budget"
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Analyze_ShouldPreserveIncompleteSearchWhenNamescopeContainsTarget(bool lookupSucceeded)
    {
        var correlations = new[]
        {
            new RenderElementCorrelation("InactiveTarget", "$.layout", "sample.item")
        };
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new { searchComplete = false, results = Array.Empty<object>() }, lookupSucceeded)
                with { Lookup = new PreviewCorrelationLookup("InactiveTarget", "exact") },
            Diagnostic("get_namescope", new
            {
                namedElements = new[] { new { elementId = "Element_Inactive", name = "InactiveTarget" } }
            })
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.ResolvedTargetCount.Should().Be(0);
        summary.NamescopeOnlyCorrelationCount.Should().Be(0);
        summary.InspectionTruncated.Should().BeTrue();
        summary.UnresolvedCorrelations.Should().ContainSingle().Which.Reason.Should().Be("search-incomplete");
    }

    private static PreviewRuntimeDiagnostic Diagnostic(string tool, object payload, bool success = true)
        => new(tool, success, JsonSerializer.SerializeToElement(payload));
}
