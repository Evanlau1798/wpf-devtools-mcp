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
    public void BuildClippingTargetIds_ShouldPreserveMoreThanOneBatchOfResolvedTargets()
    {
        var results = Enumerable.Range(1, 101)
            .Select(index => new
            {
                elementId = $"Element_{index}",
                elementName = $"Target{index}"
            })
            .ToArray();
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new { success = true, results })
        };

        var targets = UiBlueprintPreviewDiagnosticsBridge.BuildClippingTargetIds(diagnostics);

        targets.Should().HaveCount(101);
        targets.Should().StartWith("Element_1").And.EndWith("Element_101");
    }

    [Fact]
    public void BuildClippingTargetBatches_ShouldSplitAtThePublicBatchLimit()
    {
        var targets = Enumerable.Range(1, 101)
            .Select(index => $"Element_{index}")
            .ToArray();

        var batches = UiBlueprintPreviewDiagnosticsBridge.BuildClippingTargetBatches(targets);

        batches.Should().HaveCount(2);
        batches[0].Should().HaveCount(100);
        batches[1].Should().Equal("Element_101");
    }

    [Fact]
    public void BuildClippingTargetIds_ShouldExcludeUncorrelatedPrefixMatches()
    {
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                results = new[]
                {
                    new { elementId = "Element_1", elementName = "TargetA" },
                    new { elementId = "Element_2", elementName = "TargetA.Helper" }
                }
            })
        };

        var targets = UiBlueprintPreviewDiagnosticsBridge.BuildClippingTargetIds(
            diagnostics,
            new HashSet<string>(["TargetA"], StringComparer.Ordinal));

        targets.Should().Equal("Element_1");
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
    public void Analyze_ShouldExposeIncompleteCoverageForThirtyThreeExactNames()
    {
        var correlations = Enumerable.Range(1, 33)
            .Select(index => new RenderElementCorrelation(
                $"Target{index}",
                $"$.layout.slots.children[{index - 1}]",
                "nebula.item"))
            .ToArray();
        var resolved = Enumerable.Range(1, 32)
            .Select(index => new { elementId = $"Element_{index}", elementName = $"Target{index}" })
            .ToArray();
        var resolvedIds = resolved.Select(item => item.elementId).ToArray();
        var clipping = Diagnostic("get_clipping_info", new
        {
            success = true,
            results = resolvedIds.Select(elementId => new { success = true, elementId, isClipped = false })
        }) with
        {
            TargetElementIds = resolvedIds
        };

        UiBlueprintPreviewDiagnosticsBridge.BuildCorrelationLookupPlan(correlations).Should().HaveCount(32);
        var summary = PreviewLayoutRiskAnalyzer.Analyze(
            [Diagnostic("find_elements", new { success = true, results = resolved }), clipping],
            correlations);

        summary.CorrelatedTargetCount.Should().Be(33);
        summary.ResolvedTargetCount.Should().Be(32);
        summary.InspectedTargetCount.Should().Be(32);
        summary.InspectionTruncated.Should().BeTrue();
        summary.WarningsTruncated.Should().BeFalse();
        summary.UnresolvedCorrelationCount.Should().Be(1);
        summary.ReportedUnresolvedCorrelationCount.Should().Be(1);
        summary.UnresolvedCorrelationsTruncated.Should().BeFalse();
        summary.UnresolvedCorrelations.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            JsonPath = "$.layout.slots.children[32]",
            BlockKind = "nebula.item",
            ElementName = "Target33"
        });
    }

    [Fact]
    public void Analyze_ShouldNotLetDuplicateMatchesHideAnUnresolvedCorrelation()
    {
        var correlations = new[]
        {
            new RenderElementCorrelation("TargetA", "$.layout.slots.children[0]", "nebula.item"),
            new RenderElementCorrelation("TargetB", "$.layout.slots.children[1]", "nebula.item")
        };
        var resolvedIds = new[] { "Element_A1", "Element_A2" };
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                searchComplete = true,
                results = resolvedIds.Select(elementId => new { elementId, elementName = "TargetA" })
            }),
            Diagnostic("get_clipping_info", new
            {
                success = true,
                results = resolvedIds.Select(elementId => new
                {
                    success = true,
                    elementId,
                    isClipped = elementId == "Element_A1"
                })
            }) with
            {
                TargetElementIds = resolvedIds
            }
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.CorrelatedTargetCount.Should().Be(2);
        summary.ResolvedTargetCount.Should().Be(0);
        summary.InspectedTargetCount.Should().Be(0);
        summary.ClippedElementCount.Should().Be(0);
        summary.Warnings.Should().BeEmpty();
        summary.InspectionTruncated.Should().BeTrue();
        summary.UnresolvedCorrelations.Select(item => item.ElementName).Should().Equal("TargetA", "TargetB");
    }

    [Fact]
    public void Analyze_ShouldTreatDuplicateCorrelationNamesAsExactPathCoverageGaps()
    {
        var correlations = new[]
        {
            new RenderElementCorrelation("SharedName", "$.layout.slots.children[0]", "nebula.item-a"),
            new RenderElementCorrelation("SharedName", "$.layout.slots.children[1]", "orbit.item-b")
        };
        var resolvedIds = new[] { "Element_A", "Element_B" };
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                searchComplete = true,
                results = resolvedIds.Select(elementId => new { elementId, elementName = "SharedName" })
            }),
            Diagnostic("get_clipping_info", new
            {
                success = true,
                results = resolvedIds.Select(elementId => new { success = true, elementId, isClipped = false })
            }) with { TargetElementIds = resolvedIds }
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.CorrelatedTargetCount.Should().Be(1);
        summary.ResolvedTargetCount.Should().Be(0);
        summary.InspectedTargetCount.Should().Be(0);
        summary.InspectionTruncated.Should().BeTrue();
        summary.UnresolvedCorrelationCount.Should().Be(2);
        summary.UnresolvedCorrelations.Select(item => item.JsonPath).Should().Equal(
            "$.layout.slots.children[0]",
            "$.layout.slots.children[1]");
    }

    [Fact]
    public void Analyze_ShouldExposeIncompleteSearchWithDuplicateSameElementResult()
    {
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                searchComplete = false,
                results = new[]
                {
                    new { elementId = "Element_1", elementName = "TargetA" },
                    new { elementId = "Element_1", elementName = "TargetA" }
                }
            }),
            Diagnostic("get_clipping_info", new
            {
                success = true,
                isClipped = false
            }) with
            {
                TargetElementIds = ["Element_1"]
            }
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(
            diagnostics,
            [new RenderElementCorrelation("TargetA", "$.layout", "nebula.item")]);

        summary.ResolvedTargetCount.Should().Be(1);
        summary.InspectedTargetCount.Should().Be(1);
        summary.InspectionTruncated.Should().BeTrue();
    }

    [Fact]
    public void Analyze_ShouldReportCompleteCoverageForOneHundredOneInspectedTargets()
    {
        var correlations = Enumerable.Range(1, 101)
            .Select(index => new RenderElementCorrelation(
                $"WpfDevToolsBp_{index:0000}",
                $"$.layout.slots.children[{index - 1}]",
                "nebula.item"))
            .ToArray();
        var resolved = Enumerable.Range(1, 101)
            .Select(index => new
            {
                elementId = $"Element_{index}",
                elementName = $"WpfDevToolsBp_{index:0000}"
            })
            .ToArray();
        var resolvedIds = resolved.Select(item => item.elementId).ToArray();
        var clippingDiagnostics = resolvedIds
            .Chunk(100)
            .Select(batch => Diagnostic("get_clipping_info", new
            {
                success = true,
                results = batch.Select(elementId => new { success = true, elementId, isClipped = false })
            }) with
            {
                TargetElementIds = batch
            })
            .ToArray();

        var summary = PreviewLayoutRiskAnalyzer.Analyze(
            [Diagnostic("find_elements", new { success = true, results = resolved }), .. clippingDiagnostics],
            correlations);

        summary.CorrelatedTargetCount.Should().Be(101);
        summary.ResolvedTargetCount.Should().Be(101);
        summary.InspectedTargetCount.Should().Be(101);
        summary.InspectionTruncated.Should().BeFalse();
        summary.WarningsTruncated.Should().BeFalse();
        summary.UnresolvedCorrelationCount.Should().Be(0);
        summary.UnresolvedCorrelations.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_ShouldBoundUnresolvedCorrelationDetailsIndependently()
    {
        var correlations = Enumerable.Range(1, 40)
            .Select(index => new RenderElementCorrelation(
                $"Target{index:00}",
                $"$.layout.slots.children[{index - 1}]",
                $"nebula.item-{index:00}"))
            .ToArray();

        var summary = PreviewLayoutRiskAnalyzer.Analyze([], correlations);

        summary.UnresolvedCorrelationCount.Should().Be(40);
        summary.ReportedUnresolvedCorrelationCount.Should().Be(32);
        summary.UnresolvedCorrelationsTruncated.Should().BeTrue();
        summary.UnresolvedCorrelations.Should().HaveCount(32);
        summary.UnresolvedCorrelations[0].JsonPath.Should().Be("$.layout.slots.children[0]");
        summary.UnresolvedCorrelations[^1].JsonPath.Should().Be("$.layout.slots.children[31]");
    }

    [Fact]
    public void Analyze_ShouldExposeExactUninspectedRuntimeTargets()
    {
        var correlations = new[]
        {
            new RenderElementCorrelation("TargetA", "$.layout.slots.children[0]", "nebula.item-a"),
            new RenderElementCorrelation("TargetB", "$.layout.slots.children[1]", "nebula.item-b")
        };
        var diagnostics = new[]
        {
            Diagnostic("find_elements", new
            {
                success = true,
                results = new[]
                {
                    new { elementId = "Element_A", elementName = "TargetA" },
                    new { elementId = "Element_B", elementName = "TargetB" }
                }
            }),
            Diagnostic("get_clipping_info", new
            {
                success = true,
                results = new[] { new { success = true, elementId = "Element_A", isClipped = false } }
            }) with
            {
                TargetElementIds = ["Element_A"]
            }
        };

        var summary = PreviewLayoutRiskAnalyzer.Analyze(diagnostics, correlations);

        summary.UninspectedCorrelationCount.Should().Be(1);
        summary.ReportedUninspectedCorrelationCount.Should().Be(1);
        summary.UninspectedCorrelationsTruncated.Should().BeFalse();
        summary.UninspectedCorrelations.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            JsonPath = "$.layout.slots.children[1]",
            BlockKind = "nebula.item-b",
            ElementName = "TargetB",
            ElementId = "Element_B"
        });
    }

    [Fact]
    public void Analyze_ShouldBoundUninspectedCorrelationDetailsIndependently()
    {
        var correlations = Enumerable.Range(1, 40)
            .Select(index => new RenderElementCorrelation(
                $"Target{index:00}",
                $"$.layout.slots.children[{index - 1}]",
                $"nebula.item-{index:00}"))
            .ToArray();
        var resolved = correlations.Select((item, index) => new
        {
            elementId = $"Element_{index + 1:00}",
            elementName = item.ElementName
        });

        var summary = PreviewLayoutRiskAnalyzer.Analyze(
            [Diagnostic("find_elements", new { success = true, results = resolved })],
            correlations);

        summary.UninspectedCorrelationCount.Should().Be(40);
        summary.ReportedUninspectedCorrelationCount.Should().Be(32);
        summary.UninspectedCorrelationsTruncated.Should().BeTrue();
        summary.UninspectedCorrelations.Should().HaveCount(32);
        summary.UninspectedCorrelations[0].ElementId.Should().Be("Element_01");
        summary.UninspectedCorrelations[^1].ElementId.Should().Be("Element_32");
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
