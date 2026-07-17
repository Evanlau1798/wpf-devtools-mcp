using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerRenderPackSnapshotTests
{
    [Fact]
    public void ValidatePackSnapshotContinuity_WhenValidationAndRenderDiffer_ShouldFailClosed()
    {
        var validationFingerprints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sample"] = "validation-content"
        };
        var renderFingerprints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sample"] = "render-content"
        };

        var issue = UiBlueprintRenderer.ValidatePackSnapshotContinuity(
            validationFingerprints,
            renderFingerprints);

        issue.Should().NotBeNull();
        issue!.Code.Should().Be("PackContentChanged");
        issue.Message.Should().Contain("sample");
    }

    [Fact]
    public void ValidatePackSnapshotContinuity_WhenSetsMatch_ShouldAllowRender()
    {
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sample"] = "stable-content"
        };

        UiBlueprintRenderer.ValidatePackSnapshotContinuity(fingerprints, fingerprints)
            .Should().BeNull();
    }
}
