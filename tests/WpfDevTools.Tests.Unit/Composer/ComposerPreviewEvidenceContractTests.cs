using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Preview;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewEvidenceContractTests
{
    [Fact]
    public void CompiledPreview_ShouldDescribeConfigurationWithoutClaimingHostVisualEvidence()
    {
        var result = new PreviewBlueprintResult(
            true,
            true,
            true,
            true,
            string.Empty,
            "<Window />",
            [],
            new PreviewHostResult("compiled", Started: false))
        {
            UsesRuntimeDependencies = true
        };

        result.VisualFidelity.Should().Be("resource-backed");
        result.VisualValidationGuidance.Should().Contain("host was not started");
        result.VisualComparisonChecklist.Should().OnlyContain(item =>
            !item.Preview.Contains("loads", StringComparison.OrdinalIgnoreCase)
            && !item.Preview.Contains("measures", StringComparison.OrdinalIgnoreCase));
    }
}
