using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewVisualLayoutContractTests
{
    [Fact]
    public void TryParse_ShouldAcceptPackNeutralRegionAndScrollbarExpectations()
    {
        var success = PreviewVisualLayoutContractParser.TryParse(
            """
            {
              "regions": [{
                "elementName": "PrimaryRegion",
                "bounds": { "x": 0.10, "y": 0.08, "width": 0.80, "height": 0.52 },
                "tolerance": 0.04,
                "horizontalScrollbarChrome": "hidden",
                "verticalScrollbarChrome": "any"
              }]
            }
            """,
            out var contract,
            out var error);

        success.Should().BeTrue(error);
        contract!.Regions.Should().ContainSingle();
        contract.Regions[0].ElementName.Should().Be("PrimaryRegion");
        contract.Regions[0].Bounds.Height.Should().Be(0.52);
        contract.Regions[0].HorizontalScrollbarChrome.Should().Be("hidden");
    }

    [Theory]
    [InlineData("{\"regions\":[]}", "at least one")]
    [InlineData("{\"regions\":[null]}", "unsupported field or is missing bounds")]
    [InlineData("{\"regions\":[{\"elementName\":\"Primary\",\"bounds\":{\"y\":0,\"width\":1,\"height\":1}}]}", "x, y, width, and height")]
    [InlineData("{\"regions\":[{\"elementName\":\"Primary\",\"bounds\":{\"x\":0,\"width\":1,\"height\":1}}]}", "x, y, width, and height")]
    [InlineData("{\"regions\":[{\"elementName\":\"Primary\",\"bounds\":{\"x\":0,\"y\":0,\"height\":1}}]}", "x, y, width, and height")]
    [InlineData("{\"regions\":[{\"elementName\":\"Primary\",\"bounds\":{\"x\":0,\"y\":0,\"width\":1}}]}", "x, y, width, and height")]
    [InlineData("{\"regions\":[{\"elementName\":\"Primary\",\"bounds\":{\"x\":0.8,\"y\":0,\"width\":0.3,\"height\":1}}]}", "inside the normalized viewport")]
    [InlineData("{\"regions\":[{\"elementName\":\"Primary\",\"bounds\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1},\"horizontalScrollbarChrome\":\"auto\"}]}", "any, hidden, visible")]
    [InlineData("{\"regions\":[{\"elementName\":\"Primary\",\"bounds\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1},\"tolerance\":0.26}]}", "between 0 and 0.25")]
    [InlineData("{\"regions\":[{\"elementName\":\"Primary\",\"bounds\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1}},{\"elementName\":\"Primary\",\"bounds\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1}}]}", "duplicates")]
    [InlineData("{\"regions\":[{\"elementName\":\"Primary\",\"bounds\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1},\"unknown\":true}]}", "unsupported field")]
    public void TryParse_ShouldRejectInvalidContracts(string json, string expectedMessage)
    {
        PreviewVisualLayoutContractParser.TryParse(json, out _, out var error).Should().BeFalse();
        error.Should().Contain(expectedMessage);
    }

    [Fact]
    public void TryParse_ShouldRejectMoreThanMaximumRegions()
    {
        var regions = Enumerable.Range(1, PreviewVisualLayoutContractParser.MaximumRegions + 1)
            .Select(index => new
            {
                elementName = $"Region{index}",
                bounds = new { x = 0, y = 0, width = 1, height = 1 }
            });

        PreviewVisualLayoutContractParser.TryParse(
            JsonSerializer.Serialize(new { regions }),
            out _,
            out var error).Should().BeFalse();

        error.Should().Contain($"at most {PreviewVisualLayoutContractParser.MaximumRegions}");
    }

    [Fact]
    public void Analyze_ShouldPassMatchingBoundsAndHiddenScrollbarChrome()
    {
        PreviewVisualLayoutContractParser.TryParse(
            ContractJson(horizontalChrome: "hidden"),
            out var contract,
            out _).Should().BeTrue();

        var summary = PreviewVisualLayoutContractAnalyzer.Analyze(
            Diagnostics(actualHeight: 416, horizontalChrome: "Collapsed"),
            contract);

        summary.Provided.Should().BeTrue();
        summary.Passed.Should().BeTrue();
        summary.MismatchCount.Should().Be(0);
        summary.UnresolvedCount.Should().Be(0);
        summary.Regions.Should().ContainSingle(region =>
            region.Status == "matched"
            && region.Reason == null
            && region.ActualBounds != null
            && region.ActualBounds.Height == 0.52);
    }

    [Fact]
    public void Analyze_ShouldCompareViewportVisibleBoundsWhenRegionIsClipped()
    {
        PreviewVisualLayoutContractParser.TryParse(
            """{"regions":[{"elementName":"PrimaryRegion","bounds":{"x":0.10,"y":0.08,"width":0.75,"height":0.52},"tolerance":0.01}]}""",
            out var contract,
            out _).Should().BeTrue();
        var warning = new PreviewLayoutWarning(
            "RuntimeClippingDetected", "$", "core.border", "PrimaryRegion", "Border_1",
            "explicit-clip", "clipping", "unconfirmed-clipping", "advisory",
            "not-determined", true,
            JsonSerializer.SerializeToElement(new { left = 0, top = 0, right = 50, bottom = 0 }),
            null) { VisibleRatio = 0.9375 };
        var risks = new PreviewLayoutRiskSummary(1, 1, false, [warning]);

        var summary = PreviewVisualLayoutContractAnalyzer.Analyze(
            Diagnostics(actualHeight: 416, horizontalChrome: "Collapsed"),
            contract,
            risks);
        var region = summary.Regions[0];

        summary.Passed.Should().BeTrue();
        region.ActualBounds!.Width.Should().Be(0.8);
        region.ActualVisibleBounds!.Width.Should().Be(0.75);
        region.VisibleRatio.Should().Be(0.9375);
    }

    [Fact]
    public void Analyze_ShouldNotInventVisibleBoundsFromStructuralOverflow()
    {
        PreviewVisualLayoutContractParser.TryParse(
            """{"regions":[{"elementName":"PrimaryRegion","bounds":{"x":0.10,"y":0.08,"width":0.75,"height":0.52},"tolerance":0.01}]}""",
            out var contract,
            out _).Should().BeTrue();
        var warning = new PreviewLayoutWarning(
            "RuntimeStructuralOverflowRisk", "$", "core.border", "PrimaryRegion", "Border_1",
            "ancestor-layout-clip", "structural-overflow", "unconfirmed-structural", "advisory",
            "not-determined", true,
            JsonSerializer.SerializeToElement(new { left = 0, top = 0, right = 50, bottom = 0 }),
            null) { VisibleRatio = 0.9375 };
        var risks = new PreviewLayoutRiskSummary(1, 1, false, [warning]);

        var summary = PreviewVisualLayoutContractAnalyzer.Analyze(
            Diagnostics(actualHeight: 416, horizontalChrome: "Collapsed"),
            contract,
            risks);
        var region = summary.Regions[0];

        summary.Passed.Should().BeFalse();
        region.ActualBounds!.Width.Should().Be(0.8);
        region.ActualVisibleBounds.Should().BeNull();
        region.VisibleRatio.Should().BeNull();
    }

    [Fact]
    public void Analyze_ShouldExposeGeometryAndScrollbarMismatches()
    {
        PreviewVisualLayoutContractParser.TryParse(
            ContractJson(horizontalChrome: "hidden"),
            out var contract,
            out _).Should().BeTrue();

        var summary = PreviewVisualLayoutContractAnalyzer.Analyze(
            Diagnostics(actualHeight: 280, horizontalChrome: "Visible"),
            contract);

        summary.Passed.Should().BeFalse();
        summary.MismatchCount.Should().Be(1);
        summary.UnresolvedCount.Should().Be(0);
        summary.Regions[0].Status.Should().Be("mismatch");
        summary.Regions[0].MaximumBoundsDelta.Should().BeApproximately(0.17, 0.001);
        summary.Regions[0].ScrollbarMismatches.Should().ContainSingle()
            .Which.Should().Contain("horizontal");
    }

    [Fact]
    public void Analyze_ShouldKeepUnresolvedRegionsSeparateFromMismatches()
    {
        PreviewVisualLayoutContractParser.TryParse(ContractJson("any"), out var contract, out _)
            .Should().BeTrue();

        var diagnostics = Diagnostics(actualHeight: 416, horizontalChrome: "Collapsed")
            .Select(item => item.Tool == "get_element_snapshot"
                ? Diagnostic("get_element_snapshot", new
                {
                    success = true,
                    elementId = "Border_1",
                    elementName = "PrimaryRegion",
                    layout = new { actualWidth = 800, actualHeight = 416 }
                })
                : item)
            .ToArray();

        var summary = PreviewVisualLayoutContractAnalyzer.Analyze(diagnostics, contract);

        summary.Passed.Should().BeFalse();
        summary.EvaluatedRegionCount.Should().Be(0);
        summary.MismatchCount.Should().Be(0);
        summary.UnresolvedCount.Should().Be(1);
        summary.Regions[0].Status.Should().Be("unresolved");
        summary.Regions[0].Reason.Should().Contain("position");
        summary.Regions[0].ScrollbarMismatches.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_ShouldCompareUnroundedBoundsAtToleranceBoundary()
    {
        PreviewVisualLayoutContractParser.TryParse(ContractJson("any"), out var contract, out _)
            .Should().BeTrue();
        var diagnostics = new[]
        {
            Diagnostic("get_layout_info", new
            {
                success = true,
                actualWidth = 1000,
                actualHeight = 800
            }),
            Diagnostic("get_element_snapshot", new
            {
                success = true,
                elementId = "Border_1",
                elementName = "PrimaryRegion",
                properties = new { },
                layout = new
                {
                    actualWidth = 800,
                    actualHeight = 416,
                    positionInWindow = new { x = 140.04, y = 64 }
                }
            })
        };

        var summary = PreviewVisualLayoutContractAnalyzer.Analyze(diagnostics, contract);

        summary.Passed.Should().BeFalse();
        summary.MismatchCount.Should().Be(1);
    }

    [Fact]
    public void Analyze_ShouldRejectUnknownScrollbarVisibilityAsHidden()
    {
        PreviewVisualLayoutContractParser.TryParse(ContractJson("hidden"), out var contract, out _)
            .Should().BeTrue();

        var summary = PreviewVisualLayoutContractAnalyzer.Analyze(
            Diagnostics(actualHeight: 416, horizontalChrome: "Unavailable"),
            contract);

        summary.Passed.Should().BeFalse();
        summary.Regions[0].ScrollbarMismatches.Should().ContainSingle();
    }

    [Fact]
    public async Task PreviewUiBlueprint_ShouldRejectInvalidContractAtTheToolBoundary()
    {
        using var sessionManager = new SessionManager();

        var result = await UiComposerMcpTools.PreviewUiBlueprint(
            sessionManager,
            "{}",
            visualLayoutContractJson: "{\"regions\":[]}",
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.StructuredContent!.Value.GetProperty("errorCode").GetString()
            .Should().Be("InvalidArgument");
    }

    [Fact]
    public void RequiresRuntimeDiagnostics_ShouldIncludeVisualLayoutContract()
    {
        PreviewVisualLayoutContractParser.TryParse(ContractJson("any"), out var contract, out _)
            .Should().BeTrue();

        UiBlueprintPreviewService.RequiresRuntimeDiagnostics(new PreviewBlueprintRequest(
                BlueprintJson: "{}",
                VisualLayoutContract: contract))
            .Should().BeTrue();
    }

    [Fact]
    public void Analyze_ShouldNotReportPassWhenContractWasNotProvided()
    {
        var summary = PreviewVisualLayoutContractAnalyzer.Analyze([], null);

        summary.Provided.Should().BeFalse();
        summary.Passed.Should().BeNull();
    }

    [Fact]
    public void BuildVisualContractElementLookup_ShouldDeduplicateTheSameRuntimeElement()
    {
        var result = new
        {
            results = new[]
            {
                new { elementName = "PrimaryRegion", elementId = "Border_1" }
            }
        };
        var diagnostics = new[]
        {
            Diagnostic("find_elements", result),
            Diagnostic("find_elements", result)
        };

        var lookup = UiBlueprintPreviewDiagnosticsBridge.BuildVisualContractElementLookup(diagnostics);

        lookup.Should().ContainSingle().Which.Should().Be(
            new KeyValuePair<string, string>("PrimaryRegion", "Border_1"));
    }

    [Fact]
    public void BuildVisualContractElementLookup_ShouldRejectDifferentRuntimeElementsWithTheSameName()
    {
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                results = new[] { new { elementName = "PrimaryRegion", elementId = "Border_1" } }
            }),
            Diagnostic("find_elements", new
            {
                results = new[] { new { elementName = "PrimaryRegion", elementId = "Border_2" } }
            })
        };

        UiBlueprintPreviewDiagnosticsBridge.BuildVisualContractElementLookup(diagnostics)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewUiBlueprint_ShouldPreserveProvidedContractOnInvalidBlueprint()
    {
        using var sessionManager = new SessionManager();

        var result = await UiComposerMcpTools.PreviewUiBlueprint(
            sessionManager,
            "{}",
            visualLayoutContractJson: ContractJson("any"),
            cancellationToken: CancellationToken.None);

        var summary = result.StructuredContent!.Value.GetProperty("visualLayoutContractSummary");
        summary.GetProperty("provided").GetBoolean().Should().BeTrue();
        summary.GetProperty("passed").GetBoolean().Should().BeFalse();
        summary.GetProperty("unresolvedCount").GetInt32().Should().Be(1);
    }

    private static string ContractJson(string horizontalChrome)
        => $$"""
           {
             "regions": [{
               "elementName": "PrimaryRegion",
               "bounds": { "x": 0.10, "y": 0.08, "width": 0.80, "height": 0.52 },
               "tolerance": 0.04,
               "horizontalScrollbarChrome": "{{horizontalChrome}}"
             }]
           }
           """;

    private static IReadOnlyList<PreviewRuntimeDiagnostic> Diagnostics(
        double actualHeight,
        string horizontalChrome)
        =>
        [
            Diagnostic("get_layout_info", new
            {
                success = true,
                actualWidth = 1000,
                actualHeight = 800,
                positionInWindow = new { x = 0, y = 0 }
            }),
            Diagnostic("get_element_snapshot", new
            {
                success = true,
                elementId = "Border_1",
                elementName = "PrimaryRegion",
                properties = new
                {
                    ComputedHorizontalScrollBarVisibility = new { currentValue = horizontalChrome },
                    ComputedVerticalScrollBarVisibility = new { currentValue = "Collapsed" }
                },
                layout = new
                {
                    actualWidth = 800,
                    actualHeight,
                    positionInWindow = new { x = 100, y = 64 }
                }
            })
        ];

    private static PreviewRuntimeDiagnostic Diagnostic(string tool, object payload)
        => new(tool, true, JsonSerializer.SerializeToElement(payload));
}
